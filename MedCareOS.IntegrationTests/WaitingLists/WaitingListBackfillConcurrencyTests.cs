using FluentAssertions;
using MedCareOS.Application.Appointments.Commands.ConfirmAppointment;
using MedCareOS.Application.Appointments.Commands.CreateAppointment;
using MedCareOS.Application.Appointments.Overbooking;
using MedCareOS.Application.WaitingLists.Commands.AcceptWaitingListOffer;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Exceptions;
using MedCareOS.Domain.Repositories;
using MedCareOS.Infrastructure.Persistence;
using MedCareOS.Infrastructure.Persistence.Repositories;
using MedCareOS.IntegrationTests.Workers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace MedCareOS.IntegrationTests.WaitingLists;

public class WaitingListBackfillConcurrencyTests : IClassFixture<BackfillApiFactory>
{
    private readonly BackfillApiFactory _factory;

    public WaitingListBackfillConcurrencyTests(BackfillApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ParallelAcceptAndCompetingBooking_WithBarrier_ShouldAllowOnlyOneAppointmentAndPreserveWaitingListActive()
    {
        Guid blockId, offerId, wlId, competitorPatientId, staffId, boxId;
        var specialty = $"Trauma_{Guid.NewGuid():N}"[..20];

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var u1 = User.Create(Guid.NewGuid(), $"conc1_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var u2 = User.Create(Guid.NewGuid(), $"conc2_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var uDoc = User.Create(Guid.NewGuid(), $"concd_{Guid.NewGuid():N}@test.cl", UserRole.Medico);

            var cand = Patient.Create(u1.Id, $"{Random.Shared.Next(10000000, 99999999)}-1", "Cand", "Conc", new DateOnly(1990, 1, 1), "F", "+56914000001", $"c1_{Guid.NewGuid():N}@test.cl");
            var competitor = Patient.Create(u2.Id, $"{Random.Shared.Next(10000000, 99999999)}-2", "Comp", "Direct", new DateOnly(1991, 2, 2), "M", "+56914000002", $"c2_{Guid.NewGuid():N}@test.cl");
            var staff = Staff.Create(uDoc.Id, "Dr", "Trauma", $"{Random.Shared.Next(10000000, 99999999)}-3", specialty, "Medico", "+56914000003");
            var box = Box.Create("Box Conc 1", "Consulta", 1, "1");
            var startUtc = DateTime.UtcNow.AddDays(4);
            var block = ScheduleBlock.Create(
                staff.Id, box.Id, startUtc, startUtc.AddHours(1),
                Guid.NewGuid(), DateOnly.FromDateTime(startUtc), DateOnly.FromDateTime(startUtc),
                new TimeOnly(9, 0), new TimeOnly(10, 0), [startUtc.DayOfWeek]);
            var wl = WaitingList.Create(cand.Id, specialty, priority: 1);
            wl.MarkOffered(DateTimeOffset.UtcNow);
            var offer = WaitingListOffer.Create(
                RiskThresholds.DefaultTenantId, wl.Id, cand.Id, block.Id, "SlotReleased", null, 1,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));

            db.Users.AddRange(u1, u2, uDoc);
            db.Patients.AddRange(cand, competitor);
            db.Staffs.Add(staff);
            db.Boxes.Add(box);
            db.ScheduleBlocks.Add(block);
            db.WaitingLists.Add(wl);
            db.WaitingListOffers.Add(offer);
            await db.SaveChangesAsync();

            blockId = block.Id;
            offerId = offer.Id;
            wlId = wl.Id;
            competitorPatientId = competitor.Id;
            staffId = staff.Id;
            boxId = box.Id;
        }

        // Probamos una carrera entre AcceptWaitingListOfferCommand y una segunda cita creada sobre un bloque recién ocupado (o Accept paralelo)
        using var barrier = new Barrier(2);

        var t1 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            using var s = _factory.Services.CreateScope();
            var sender = s.ServiceProvider.GetRequiredService<ISender>();
            return await sender.Send(new AcceptWaitingListOfferCommand(offerId, null, null, null, "Control"));
        });

        var t2 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await Task.Delay(5);
            using var s = _factory.Services.CreateScope();
            var sender = s.ServiceProvider.GetRequiredService<ISender>();
            return await sender.Send(new CreateAppointmentCommand(blockId, competitorPatientId, "Direct", "paciente"));
        });

        await Task.WhenAll(
            t1.ContinueWith(_ => { }),
            t2.ContinueWith(_ => { }));

        // Exactamente uno pudo crear cita (si t1 ganó, t2 recibió DuplicateAppointmentException; o t1 recibió DuplicateAppointmentException y wl volvió a Active)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var activeApps = await db.Appointments.AsNoTracking()
                .Where(a => a.ScheduleBlockId == blockId && a.Status != AppointmentStatus.Cancelled)
                .ToListAsync();
            activeApps.Should().HaveCount(1);

            if (t1.IsCompletedSuccessfully)
            {
                t2.IsFaulted.Should().BeTrue();
                t2.Exception!.InnerException.Should().BeOfType<DuplicateAppointmentException>();
            }
            else
            {
                t2.IsCompletedSuccessfully.Should().BeTrue();
                var wl = await db.WaitingLists.AsNoTracking().FirstAsync(w => w.Id == wlId);
                wl.Status.Should().Be(WaitingListStatus.Active);
            }
        }
    }

    [Fact]
    public async Task ParallelAcceptOfferAndLateConfirmation_WithBarrier_ShouldSerializeCleanlyWithoutInconsistentState()
    {
        Guid origAppId, origPatientId, obBlockId, offerId, wlId;
        var specialty = $"Oftal_{Guid.NewGuid():N}"[..20];

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var uOrig = User.Create(Guid.NewGuid(), $"co1_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var uCand = User.Create(Guid.NewGuid(), $"co2_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var uDoc = User.Create(Guid.NewGuid(), $"cod_{Guid.NewGuid():N}@test.cl", UserRole.Medico);

            var pOrig = Patient.Create(uOrig.Id, $"{Random.Shared.Next(10000000, 99999999)}-4", "Orig", "Race", new DateOnly(1988, 1, 1), "M", "+56915000001", $"ro_{Guid.NewGuid():N}@test.cl");
            var pCand = Patient.Create(uCand.Id, $"{Random.Shared.Next(10000000, 99999999)}-5", "Cand", "Race", new DateOnly(1992, 2, 2), "F", "+56915000002", $"rc_{Guid.NewGuid():N}@test.cl");
            var staff = Staff.Create(uDoc.Id, "Dr", "Oftal", $"{Random.Shared.Next(10000000, 99999999)}-6", specialty, "Medico", "+56915000003");
            var box = Box.Create("Box Race", "Consulta", 2, "1");
            var startUtc = DateTime.UtcNow.AddDays(4);
            var regularBlock = ScheduleBlock.Create(
                staff.Id, box.Id, startUtc, startUtc.AddHours(1),
                Guid.NewGuid(), DateOnly.FromDateTime(startUtc), DateOnly.FromDateTime(startUtc),
                new TimeOnly(12, 0), new TimeOnly(13, 0), [startUtc.DayOfWeek]);
            var origApp = Appointment.Create(regularBlock.Id, pOrig.Id, "Control");
            origApp.MarkFinalConfirmationRequested(DateTimeOffset.UtcNow.AddHours(-2));
            origApp.MarkUnresponsive(DateTimeOffset.UtcNow.AddMinutes(-15));
            var obBlock = ScheduleBlock.CreateOverbook(regularBlock, origApp.Id);
            obBlock.MarkOverbookOffered(DateTimeOffset.UtcNow);
            var wl = WaitingList.Create(pCand.Id, specialty, priority: 1);
            wl.MarkOffered(DateTimeOffset.UtcNow);
            var offer = WaitingListOffer.Create(
                RiskThresholds.DefaultTenantId, wl.Id, pCand.Id, obBlock.Id, "Overbook", origApp.Id, 1,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));

            db.Users.AddRange(uOrig, uCand, uDoc);
            db.Patients.AddRange(pOrig, pCand);
            db.Staffs.Add(staff);
            db.Boxes.Add(box);
            db.ScheduleBlocks.AddRange(regularBlock, obBlock);
            db.Appointments.Add(origApp);
            db.WaitingLists.Add(wl);
            db.WaitingListOffers.Add(offer);
            await db.SaveChangesAsync();

            origAppId = origApp.Id;
            origPatientId = pOrig.Id;
            obBlockId = obBlock.Id;
            offerId = offer.Id;
            wlId = wl.Id;
        }

        using var barrier = new Barrier(2);

        var acceptTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            using var s = _factory.Services.CreateScope();
            var sender = s.ServiceProvider.GetRequiredService<ISender>();
            return await sender.Send(new AcceptWaitingListOfferCommand(offerId, null, null, null, "Control"));
        });

        var lateConfirmTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            using var s = _factory.Services.CreateScope();
            var sender = s.ServiceProvider.GetRequiredService<ISender>();
            var confirmedAt = DateTimeOffset.UtcNow;
            await sender.Send(new ConfirmAppointmentCommand(origAppId, origPatientId, Source: "public_token", OccurredAtUtc: confirmedAt));
            var resolution = s.ServiceProvider.GetRequiredService<IOverbookCollisionResolutionService>();
            await resolution.HandleLateConfirmationAsync(origAppId, confirmedAt, Guid.NewGuid());
        });

        await Task.WhenAll(
            acceptTask.ContinueWith(_ => { }),
            lateConfirmTask.ContinueWith(_ => { }));

        lateConfirmTask.IsCompletedSuccessfully.Should().BeTrue();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var obBlock = await db.ScheduleBlocks.AsNoTracking().FirstAsync(b => b.Id == obBlockId);
            var offer = await db.WaitingListOffers.AsNoTracking().FirstAsync(o => o.Id == offerId);
            var wl = await db.WaitingLists.AsNoTracking().FirstAsync(w => w.Id == wlId);
            var obApps = await db.Appointments.AsNoTracking()
                .Where(a => a.ScheduleBlockId == obBlockId && a.Status != AppointmentStatus.Cancelled)
                .ToListAsync();

            if (obBlock.OverbookStatus == OverbookBlockStatus.Deactivated)
            {
                // Original ganó la carrera -> ventana de deshacer
                obApps.Should().BeEmpty();
                offer.Status.Should().Be(WaitingListOfferStatus.CancelledByOriginalConfirmation);
                wl.Status.Should().Be(WaitingListStatus.Active);
                acceptTask.IsFaulted.Should().BeTrue();
            }
            else
            {
                // Oferta aceptada primero -> sobrecupo ocupado + evento de colisión
                obBlock.OverbookStatus.Should().Be(OverbookBlockStatus.Occupied);
                obApps.Should().HaveCount(1);
                offer.Status.Should().Be(WaitingListOfferStatus.Accepted);
                wl.Status.Should().Be(WaitingListStatus.Accepted);
                (await db.OverbookCollisionEvents.AsNoTracking()
                    .AnyAsync(c => c.OriginalAppointmentId == origAppId))
                    .Should().BeTrue();
            }
        }
    }

    [DockerFact]
    public async Task Postgres16_PartialUniqueAndCheckConstraints_ShouldRejectConcurrentDuplicatePendingOffersAndDualTargetTokens()
    {
        await using var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(opt => opt.UseNpgsql(container.GetConnectionString()));
        await using var sp = services.BuildServiceProvider();

        using (var initScope = sp.CreateScope())
        {
            var db = initScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
        }

        Guid blockId, wl1Id, wl2Id, p1Id, p2Id;
        using (var seedScope = sp.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var u1 = User.Create(Guid.NewGuid(), $"pg1_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var u2 = User.Create(Guid.NewGuid(), $"pg2_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var uDoc = User.Create(Guid.NewGuid(), $"pgd_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
            var p1 = Patient.Create(u1.Id, "21111111-1", "P1", "Pg", new DateOnly(1990, 1, 1), "M", "+56916000001");
            var p2 = Patient.Create(u2.Id, "22222222-2", "P2", "Pg", new DateOnly(1991, 1, 1), "F", "+56916000002");
            var staff = Staff.Create(uDoc.Id, "Dr", "Pg", "23333333-3", "Cardiologia", "Medico", "+56916000003");
            var box = Box.Create("Box PG", "Consulta", 1, "1");
            var start = DateTime.UtcNow.AddDays(3);
            var block = ScheduleBlock.Create(
                staff.Id, box.Id, start, start.AddHours(1),
                Guid.NewGuid(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(start),
                new TimeOnly(10, 0), new TimeOnly(11, 0), [start.DayOfWeek]);
            var wl1 = WaitingList.Create(p1.Id, "Cardiologia");
            var wl2 = WaitingList.Create(p2.Id, "Cardiologia");

            db.Users.AddRange(u1, u2, uDoc);
            db.Patients.AddRange(p1, p2);
            db.Staffs.Add(staff);
            db.Boxes.Add(box);
            db.ScheduleBlocks.Add(block);
            db.WaitingLists.AddRange(wl1, wl2);
            await db.SaveChangesAsync();

            blockId = block.Id;
            wl1Id = wl1.Id;
            wl2Id = wl2.Id;
            p1Id = p1.Id;
            p2Id = p2.Id;
        }

        using (var offerScope1 = sp.CreateScope())
        {
            var db = offerScope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.WaitingListOffers.Add(WaitingListOffer.Create(
                RiskThresholds.DefaultTenantId, wl1Id, p1Id, blockId, "SlotReleased", null, 1,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
            await db.SaveChangesAsync();
        }

        // Segunda oferta Pending sobre el mismo bloque viola UX_waiting_list_offers_active_block en PostgreSQL 16
        using (var offerScope2 = sp.CreateScope())
        {
            var db = offerScope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.WaitingListOffers.Add(WaitingListOffer.Create(
                RiskThresholds.DefaultTenantId, wl2Id, p2Id, blockId, "SlotReleased", null, 2,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
            Func<Task> act = () => db.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>();
        }
    }
}
