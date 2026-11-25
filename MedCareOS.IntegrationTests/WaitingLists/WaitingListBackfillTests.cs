using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using MassTransit;
using MedCareOS.Application.Appointments.Commands.ConfirmAppointment;
using MedCareOS.Application.Appointments.Consumers;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.Appointments.Queries.GetDailyAgenda;
using MedCareOS.Application.Notifications.Consumers;
using MedCareOS.Application.WaitingLists.Commands.AcceptWaitingListOffer;
using MedCareOS.Application.WaitingLists.Commands.DeclineWaitingListOffer;
using MedCareOS.Application.WaitingLists.Consumers;
using MedCareOS.Application.WaitingLists.Queries.GetMyWaitingListOffers;
using MedCareOS.Application.WaitingLists.Services;
using MedCareOS.Application.WaitingLists.Workers;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Messaging.Consumers;
using MedCareOS.Infrastructure.Notifications.Channels;
using MedCareOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedCareOS.IntegrationTests.WaitingLists;

public sealed class BackfillApiFactory : CustomApiFactory
{
    protected override void ConfigureTestMassTransit(IBusRegistrationConfigurator configurator)
    {
        configurator.AddConsumer<OverbookOpenedConsumer, OverbookOpenedConsumerDefinition>();
        configurator.AddConsumer<LateConfirmationReceivedConsumer, LateConfirmationReceivedConsumerDefinition>();
        configurator.AddConsumer<SendNotificationConsumer, SendNotificationConsumerDefinition>();
    }
}

public partial class WaitingListBackfillTests : IClassFixture<BackfillApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly BackfillApiFactory _factory;

    public WaitingListBackfillTests(BackfillApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OverbookCascade_DeclineThenExpireThenAcceptPublicToken_ShouldBackfillAndEnforceSingleUse()
    {
        var specialty = $"Cardiologia_{Guid.NewGuid():N}"[..24];
        var cand1UserId = Guid.NewGuid();
        var cand2UserId = Guid.NewGuid();
        var cand3UserId = Guid.NewGuid();
        Guid cand1Id, cand2Id, cand3Id;
        Guid wl1Id, wl2Id, wl3Id;
        Guid overbookBlockId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var origUser = User.Create(Guid.NewGuid(), $"orig_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
            var u1 = User.Create(cand1UserId, $"c1_{cand1UserId:N}@test.cl", UserRole.Paciente);
            var u2 = User.Create(cand2UserId, $"c2_{cand2UserId:N}@test.cl", UserRole.Paciente);
            var u3 = User.Create(cand3UserId, $"c3_{cand3UserId:N}@test.cl", UserRole.Paciente);

            var origPatient = Patient.Create(origUser.Id, $"{Random.Shared.Next(10000000, 99999999)}-1", "Orig", "Pac", new DateOnly(1990, 1, 1), "M", "+56911000001", $"orig_{Guid.NewGuid():N}@test.cl");
            var p1 = Patient.Create(cand1UserId, $"{Random.Shared.Next(10000000, 99999999)}-2", "Ana", "Uno", new DateOnly(1988, 2, 2), "F", "+56911000002", $"c1_{Guid.NewGuid():N}@test.cl");
            var p2 = Patient.Create(cand2UserId, $"{Random.Shared.Next(10000000, 99999999)}-3", "Beto", "Dos", new DateOnly(1986, 3, 3), "M", "+56911000003", $"c2_{Guid.NewGuid():N}@test.cl");
            var p3 = Patient.Create(cand3UserId, $"{Random.Shared.Next(10000000, 99999999)}-4", "Carla", "Tres", new DateOnly(1992, 4, 4), "F", "+56911000004", $"c3_{Guid.NewGuid():N}@test.cl");

            var staff = Staff.Create(staffUser.Id, "Dr", "Casa", $"{Random.Shared.Next(10000000, 99999999)}-5", specialty, "Medico", "+56911000005");
            var box = Box.Create("Box BF 1", "Consulta", 2, "1");
            var startUtc = DateTime.UtcNow.AddDays(3);
            var endUtc = startUtc.AddHours(1);
            var regularBlock = ScheduleBlock.Create(
                staff.Id, box.Id, startUtc, endUtc,
                Guid.NewGuid(), DateOnly.FromDateTime(startUtc), DateOnly.FromDateTime(startUtc),
                new TimeOnly(10, 0), new TimeOnly(11, 0), [startUtc.DayOfWeek]);
            var origApp = Appointment.Create(regularBlock.Id, origPatient.Id, "Control");
            origApp.MarkFinalConfirmationRequested(DateTimeOffset.UtcNow.AddHours(-2));
            origApp.MarkUnresponsive(DateTimeOffset.UtcNow.AddMinutes(-30));
            var obBlock = ScheduleBlock.CreateOverbook(regularBlock, origApp.Id);

            // Prioridad: p1 (3) > p2 (2) > p3 (1)
            var wl1 = WaitingList.Create(p1.Id, specialty, priority: 3, utcNow: DateTimeOffset.UtcNow.AddDays(-1));
            var wl2 = WaitingList.Create(p2.Id, specialty, priority: 2, utcNow: DateTimeOffset.UtcNow.AddDays(-2));
            var wl3 = WaitingList.Create(p3.Id, specialty, priority: 1, utcNow: DateTimeOffset.UtcNow.AddDays(-3));

            db.Users.AddRange(origUser, staffUser, u1, u2, u3);
            db.Patients.AddRange(origPatient, p1, p2, p3);
            db.Staffs.Add(staff);
            db.Boxes.Add(box);
            db.ScheduleBlocks.AddRange(regularBlock, obBlock);
            db.Appointments.Add(origApp);
            db.WaitingLists.AddRange(wl1, wl2, wl3);
            await db.SaveChangesAsync();

            cand1Id = p1.Id;
            cand2Id = p2.Id;
            cand3Id = p3.Id;
            wl1Id = wl1.Id;
            wl2Id = wl2.Id;
            wl3Id = wl3.Id;
            overbookBlockId = obBlock.Id;

            var pub = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            await pub.Publish(new OverbookOpened(
                OverbookBlockId: obBlock.Id,
                TenantId: RiskThresholds.DefaultTenantId,
                Specialty: specialty,
                StaffId: staff.Id,
                BoxId: box.Id,
                StartTimeUtc: obBlock.StartTime,
                EndTimeUtc: obBlock.EndTime,
                SourceAppointmentId: origApp.Id,
                DecisionMode: OverbookDecisionMode.Autonomous,
                CorrelationId: Guid.NewGuid(),
                OccurredAtUtc: DateTimeOffset.UtcNow));
        }

        // 1. Esperar primera oferta (para cand1) y notificación con link token
        var offer1 = await PollAsync(async () =>
        {
            using var s = _factory.Services.CreateScope();
            var db = s.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.WaitingListOffers.AsNoTracking()
                .FirstOrDefaultAsync(o => o.ScheduleBlockId == overbookBlockId && o.PatientId == cand1Id && o.NotificationId != null);
        });
        offer1.Should().NotBeNull();
        offer1!.CascadeAttemptNumber.Should().Be(1);

        var rawToken1 = await PollOfferTokenFromEmailAsync(offer1.NotificationId!.Value);

        // Preview público de oferta 1
        using var publicClient = _factory.CreateClient();
        var previewRes = await publicClient.GetAsync($"/api/public/waiting-list/offers/preview?token={Uri.EscapeDataString(rawToken1)}");
        previewRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewRes.Content.ReadFromJsonAsync<JsonElement>();
        previewJson.GetProperty("waitingListOfferId").GetGuid().Should().Be(offer1.Id);
        previewJson.GetProperty("targetKind").GetString().Should().Be("WaitingListOffer");

        // IDOR check: candidato 2 intenta aceptar la oferta de candidato 1 vía portal -> 403
        using var cand2Client = CreateAuthClient(cand2UserId, "Paciente");
        var idorAcceptRes = await cand2Client.PostAsJsonAsync($"/api/waiting-list/offers/{offer1.Id}/accept", new { SymptomsRaw = "Hack" });
        idorAcceptRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Candidato 1 rechaza vía enlace público -> vuelve a Active y dispara cascada hacia Candidato 2
        var decline1Res = await publicClient.PostAsJsonAsync("/api/public/waiting-list/offers/decline", new
        {
            Token = rawToken1,
            Reason = "No puedo asistir a esa hora"
        });
        decline1Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var decline1Body = await decline1Res.Content.ReadFromJsonAsync<DeclineWaitingListOfferResult>(JsonOptions);
        decline1Body!.CascadeOutcome.Should().Be("Offered");
        var offer2Id = decline1Body.NextOfferId!.Value;

        // Reutilizar el mismo token público -> 410 Gone
        var replayRes = await publicClient.PostAsJsonAsync("/api/public/waiting-list/offers/accept", new { Token = rawToken1 });
        replayRes.StatusCode.Should().Be(HttpStatusCode.Gone);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var wl1 = await db.WaitingLists.AsNoTracking().FirstAsync(w => w.Id == wl1Id);
            wl1.Status.Should().Be(WaitingListStatus.Active);
            wl1.Priority.Should().Be(3);
        }

        // Verificar endpoint autenticado /api/waiting-list/offers/me para candidato 2
        var myOffersRes = await cand2Client.GetAsync("/api/waiting-list/offers/me");
        myOffersRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var myOffers = await myOffersRes.Content.ReadFromJsonAsync<List<WaitingListOfferDto>>(JsonOptions);
        myOffers.Should().ContainSingle(o => o.Id == offer2Id);

        // 3. Forzar expiración de oferta 2 y correr Worker processor -> expira y ofrece a Candidato 3
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var o2 = await db.WaitingListOffers.FirstAsync(o => o.Id == offer2Id);
            typeof(WaitingListOffer).GetProperty(nameof(WaitingListOffer.ExpiresAt))!
                .SetValue(o2, DateTimeOffset.UtcNow.AddMinutes(-2));
            await db.SaveChangesAsync();

            var processor = scope.ServiceProvider.GetRequiredService<IWaitingListOfferExpirationProcessor>();
            var expiredProcessed = await processor.ProcessBatchAsync(10);
            expiredProcessed.Should().BeGreaterThanOrEqualTo(1);
        }

        WaitingListOffer? offer3;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var o2Reloaded = await db.WaitingListOffers.AsNoTracking().FirstAsync(o => o.Id == offer2Id);
            o2Reloaded.Status.Should().Be(WaitingListOfferStatus.Expired);
            var wl2Reloaded = await db.WaitingLists.AsNoTracking().FirstAsync(w => w.Id == wl2Id);
            wl2Reloaded.Status.Should().Be(WaitingListStatus.Active);

            offer3 = await db.WaitingListOffers.AsNoTracking()
                .FirstOrDefaultAsync(o => o.ScheduleBlockId == overbookBlockId && o.PatientId == cand3Id);
            offer3.Should().NotBeNull();
            offer3!.CascadeAttemptNumber.Should().Be(3);
            offer3.Status.Should().Be(WaitingListOfferStatus.Pending);
        }

        // 4. Candidato 3 acepta vía endpoint autenticado + retry idempotente
        using var cand3Client = CreateAuthClient(cand3UserId, "Paciente");
        var accept3Res = await cand3Client.PostAsJsonAsync($"/api/waiting-list/offers/{offer3!.Id}/accept", new { SymptomsRaw = "Control cardiologico" });
        accept3Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var accept3Body = await accept3Res.Content.ReadFromJsonAsync<AcceptWaitingListOfferResult>(JsonOptions);
        accept3Body!.IsSuccess.Should().BeTrue();
        accept3Body.WasIdempotentReplay.Should().BeFalse();
        var createdAppId = accept3Body.AppointmentId!.Value;

        // Replay idempotente vía endpoint autenticado -> 200 con WasIdempotentReplay = true y misma cita
        var accept3Retry = await cand3Client.PostAsJsonAsync($"/api/waiting-list/offers/{offer3.Id}/accept", new { SymptomsRaw = "Control cardiologico" });
        accept3Retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var retryBody = await accept3Retry.Content.ReadFromJsonAsync<AcceptWaitingListOfferResult>(JsonOptions);
        retryBody!.WasIdempotentReplay.Should().BeTrue();
        retryBody.AppointmentId.Should().Be(createdAppId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var createdApp = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == createdAppId);
            createdApp.Status.Should().Be(AppointmentStatus.Confirmed);
            createdApp.ConfirmationState.Should().Be(ConfirmationState.FinalConfirmed);
            createdApp.OfferAcceptedAt.Should().NotBeNull();
            createdApp.OfferAcceptedAt!.Value.Offset.Should().Be(TimeSpan.Zero);
            createdApp.WaitingListOfferId.Should().Be(offer3.Id);

            var obBlock = await db.ScheduleBlocks.AsNoTracking().FirstAsync(b => b.Id == overbookBlockId);
            obBlock.OverbookStatus.Should().Be(OverbookBlockStatus.Occupied);
            obBlock.OccupiedAppointmentId.Should().Be(createdAppId);

            var wl3 = await db.WaitingLists.AsNoTracking().FirstAsync(w => w.Id == wl3Id);
            wl3.Status.Should().Be(WaitingListStatus.Accepted);
        }
    }

    [Fact]
    public async Task LateConfirmation_WhenOfferPending_ShouldCancelOfferRevokeTokenRestoreWaitingListAndSendApology()
    {
        var specialty = $"Neuro_{Guid.NewGuid():N}"[..20];
        var candUserId = Guid.NewGuid();
        Guid origAppId, origPatientId, obBlockId, wlId, offerId;
        string rawOfferToken;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var origUser = User.Create(Guid.NewGuid(), $"lateo_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var candUser = User.Create(candUserId, $"latec_{candUserId:N}@test.cl", UserRole.Paciente);
            var staffUser = User.Create(Guid.NewGuid(), $"lated_{Guid.NewGuid():N}@test.cl", UserRole.Medico);

            var origPatient = Patient.Create(origUser.Id, $"{Random.Shared.Next(10000000, 99999999)}-6", "Orig", "Tarde", new DateOnly(1990, 1, 1), "M", "+56912000001", $"lateo_{Guid.NewGuid():N}@test.cl");
            var candPatient = Patient.Create(candUserId, $"{Random.Shared.Next(10000000, 99999999)}-7", "Cand", "Espera", new DateOnly(1991, 2, 2), "F", "+56912000002", $"latec_{Guid.NewGuid():N}@test.cl");
            var staff = Staff.Create(staffUser.Id, "Dr", "Neuro", $"{Random.Shared.Next(10000000, 99999999)}-8", specialty, "Medico", "+56912000003");
            var box = Box.Create("Box Undo", "Consulta", 2, "1");
            var startUtc = DateTime.UtcNow.AddDays(4);
            var regularBlock = ScheduleBlock.Create(
                staff.Id, box.Id, startUtc, startUtc.AddHours(1),
                Guid.NewGuid(), DateOnly.FromDateTime(startUtc), DateOnly.FromDateTime(startUtc),
                new TimeOnly(11, 0), new TimeOnly(12, 0), [startUtc.DayOfWeek]);
            var origApp = Appointment.Create(regularBlock.Id, origPatient.Id, "Control");
            origApp.MarkFinalConfirmationRequested(DateTimeOffset.UtcNow.AddHours(-3));
            origApp.MarkUnresponsive(DateTimeOffset.UtcNow.AddMinutes(-20));
            var obBlock = ScheduleBlock.CreateOverbook(regularBlock, origApp.Id);
            var wl = WaitingList.Create(candPatient.Id, specialty, priority: 2, utcNow: DateTimeOffset.UtcNow.AddDays(-5));

            db.Users.AddRange(origUser, candUser, staffUser);
            db.Patients.AddRange(origPatient, candPatient);
            db.Staffs.Add(staff);
            db.Boxes.Add(box);
            db.ScheduleBlocks.AddRange(regularBlock, obBlock);
            db.Appointments.Add(origApp);
            db.WaitingLists.Add(wl);
            await db.SaveChangesAsync();

            origAppId = origApp.Id;
            origPatientId = origPatient.Id;
            obBlockId = obBlock.Id;
            wlId = wl.Id;

            var backfill = scope.ServiceProvider.GetRequiredService<IWaitingListBackfillService>();
            var offered = await backfill.OfferNextCandidateAsync(
                obBlock.Id, RiskThresholds.DefaultTenantId, "Overbook", origApp.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);
            offered.Outcome.Should().Be("Offered");
            offerId = offered.OfferId!.Value;
        }

        var offerNotifId = await PollAsync(async () =>
        {
            using var s = _factory.Services.CreateScope();
            var db = s.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var o = await db.WaitingListOffers.AsNoTracking().FirstAsync(x => x.Id == offerId);
            return o.NotificationId;
        });
        rawOfferToken = await PollOfferTokenFromEmailAsync(offerNotifId!.Value);

        // Paciente original confirma tarde
        using (var scope = _factory.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new ConfirmAppointmentCommand(origAppId, origPatientId, Source: "public_token", OccurredAtUtc: DateTimeOffset.UtcNow));
        }

        // Esperar deshacer sobrecupo + cancelación oferta + notificación disculpa
        var cancelledTuple = await PollAsync(async () =>
        {
            using var s = _factory.Services.CreateScope();
            var db = s.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var o = await db.WaitingListOffers.AsNoTracking().FirstAsync(x => x.Id == offerId);
            var obBlock = await db.ScheduleBlocks.AsNoTracking().FirstAsync(b => b.Id == obBlockId);
            return o.Status == WaitingListOfferStatus.CancelledByOriginalConfirmation &&
                   obBlock.OverbookStatus == OverbookBlockStatus.Deactivated
                ? o
                : null;
        });
        cancelledTuple.Should().NotBeNull();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ob = await db.ScheduleBlocks.AsNoTracking().FirstAsync(b => b.Id == obBlockId);
            ob.OverbookStatus.Should().Be(OverbookBlockStatus.Deactivated);

            var wl = await db.WaitingLists.AsNoTracking().FirstAsync(w => w.Id == wlId);
            wl.Status.Should().Be(WaitingListStatus.Active);
            wl.Priority.Should().Be(2);

            var apologyExists = await db.Notifications.AsNoTracking()
                .AnyAsync(n => n.WaitingListOfferId == offerId && n.Type == NotificationType.OverbookOfferCancelled);
            apologyExists.Should().BeTrue();
        }

        // Token público previamente emitido quedó revocado -> 410 Gone
        using var publicClient = _factory.CreateClient();
        var staleTryRes = await publicClient.PostAsJsonAsync("/api/public/waiting-list/offers/accept", new { Token = rawOfferToken });
        staleTryRes.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task SoftHold_WhenRegularReleasedSlotHasPendingOffer_ShouldHideFromAvailabilityAndBlockDirectCreate()
    {
        var specialty = $"Derm_{Guid.NewGuid():N}"[..20];
        var otherPatientUserId = Guid.NewGuid();
        Guid blockId, otherPatientId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var candUser = User.Create(Guid.NewGuid(), $"shc_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var otherUser = User.Create(otherPatientUserId, $"sho_{otherPatientUserId:N}@test.cl", UserRole.Paciente);
            var staffUser = User.Create(Guid.NewGuid(), $"shd_{Guid.NewGuid():N}@test.cl", UserRole.Medico);

            var cand = Patient.Create(candUser.Id, $"{Random.Shared.Next(10000000, 99999999)}-9", "Hold", "Cand", new DateOnly(1989, 5, 5), "F", "+56913000001", $"shc_{Guid.NewGuid():N}@test.cl");
            var other = Patient.Create(otherPatientUserId, $"{Random.Shared.Next(10000000, 99999999)}-0", "Hold", "Other", new DateOnly(1990, 6, 6), "M", "+56913000002", $"sho_{Guid.NewGuid():N}@test.cl");
            var staff = Staff.Create(staffUser.Id, "Dra", "Piel", $"{Random.Shared.Next(10000000, 99999999)}-1", specialty, "Medico", "+56913000003");
            var box = Box.Create("Box SoftHold", "Consulta", 1, "1");
            var startUtc = DateTime.UtcNow.AddDays(5).Date.AddHours(14);
            var block = ScheduleBlock.Create(
                staff.Id, box.Id, startUtc, startUtc.AddHours(1),
                Guid.NewGuid(), DateOnly.FromDateTime(startUtc), DateOnly.FromDateTime(startUtc),
                new TimeOnly(14, 0), new TimeOnly(15, 0), [startUtc.DayOfWeek]);
            var wl = WaitingList.Create(cand.Id, specialty, priority: 1);

            db.Users.AddRange(candUser, otherUser, staffUser);
            db.Patients.AddRange(cand, other);
            db.Staffs.Add(staff);
            db.Boxes.Add(box);
            db.ScheduleBlocks.Add(block);
            db.WaitingLists.Add(wl);
            await db.SaveChangesAsync();

            blockId = block.Id;
            otherPatientId = other.Id;

            var backfill = scope.ServiceProvider.GetRequiredService<IWaitingListBackfillService>();
            var offered = await backfill.OfferNextCandidateAsync(
                block.Id, RiskThresholds.DefaultTenantId, "SlotReleased", null, Guid.NewGuid(), DateTimeOffset.UtcNow);
            offered.Outcome.Should().Be("Offered");
        }

        // Mientras la oferta está Pending (soft-hold):
        // 1) SearchAvailability oculta el bloque
        using var publicOtherClient = CreateAuthClient(otherPatientUserId, "Paciente");
        var targetDate = DateTime.UtcNow.AddDays(5).Date;
        var availRes = await publicOtherClient.GetAsync($"/api/agenda/disponibilidad?especialidad={Uri.EscapeDataString(specialty)}&desde={targetDate:yyyy-MM-dd}&hasta={targetDate:yyyy-MM-dd}");
        availRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = await availRes.Content.ReadFromJsonAsync<List<AgendaBloqueDto>>(JsonOptions);
        slots.Should().NotContain(s => s.ScheduleBlockId == blockId);

        // 2) CreateAppointment sobre ese bloque devuelve 409 Conflict
        var directBookRes = await publicOtherClient.PostAsJsonAsync("/api/appointments", new
        {
            ScheduleBlockId = blockId,
            PatientId = otherPatientId,
            SymptomsRaw = "Intento directo",
            SchedulingSource = "paciente"
        });
        directBookRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Expirar la oferta sin más candidatos -> libera soft-hold
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pending = await db.WaitingListOffers.FirstAsync(o => o.ScheduleBlockId == blockId);
            typeof(WaitingListOffer).GetProperty(nameof(WaitingListOffer.ExpiresAt))!
                .SetValue(pending, DateTimeOffset.UtcNow.AddMinutes(-1));
            await db.SaveChangesAsync();

            var processor = scope.ServiceProvider.GetRequiredService<IWaitingListOfferExpirationProcessor>();
            await processor.ProcessBatchAsync(10);
        }

        var availAfterRes = await publicOtherClient.GetAsync($"/api/agenda/disponibilidad?especialidad={Uri.EscapeDataString(specialty)}&desde={targetDate:yyyy-MM-dd}&hasta={targetDate:yyyy-MM-dd}");
        var slotsAfter = await availAfterRes.Content.ReadFromJsonAsync<List<AgendaBloqueDto>>(JsonOptions);
        slotsAfter.Should().Contain(s => s.ScheduleBlockId == blockId);
    }

    private async Task<string> PollOfferTokenFromEmailAsync(Guid notificationId)
    {
        var store = _factory.Services.GetRequiredService<IFakeNotificationStore>();
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            var msg = store.SentMessages.FirstOrDefault(m => m.NotificationId == notificationId);
            if (msg is not null)
            {
                var match = OfferTokenRegex().Match(msg.Rendered.PlainTextBody);
                if (match.Success)
                {
                    return Uri.UnescapeDataString(match.Groups[1].Value);
                }
            }
            await Task.Delay(25);
        }

        throw new TimeoutException($"No se encontró token para la notificación de oferta {notificationId}.");
    }

    private static async Task<T?> PollAsync<T>(Func<Task<T?>> probe) where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            var result = await probe();
            if (result is not null)
                return result;
            await Task.Delay(25);
        }
        return null;
    }

    private static async Task<Guid?> PollAsync(Func<Task<Guid?>> probe)
    {
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            var result = await probe();
            if (result.HasValue)
                return result;
            await Task.Delay(25);
        }
        return null;
    }

    private HttpClient CreateAuthClient(Guid userId, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [GeneratedRegex(@"token=([^&\s]+)")]
    private static partial Regex OfferTokenRegex();
}
