using FluentAssertions;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Infrastructure.Persistence;
using MedCareOS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MedCareOS.IntegrationTests.Workers;

public class ScheduledActionsPostgresSkipLockedTests
{
    [DockerFact]
    public async Task MigrateAsync_AppliesAllMigrationsAndSeedsDefaultTenantPolicyOnPostgres16()
    {
        await using var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();

        await using var sp = BuildProvider(container.GetConnectionString());
        await ApplyMigrationsAsync(sp);

        using var scope = sp.CreateScope();
        var policyRepo = scope.ServiceProvider.GetRequiredService<ITenantConfirmationPolicyRepository>();
        var policy = await policyRepo.GetByTenantIdAsync(RiskThresholds.DefaultTenantId);

        policy.Should().NotBeNull();
        policy!.Reminder1OffsetHours.Should().Be(72);
        policy.DeadlineOffsetHours.Should().Be(12);
        policy.WeightConfirmedEarly.Should().Be(-1.5000m);
    }

    [DockerFact]
    public async Task ClaimDueBatchAsync_WhenTwoWorkersRaceInParallel_ClaimsDisjointActionsWithoutDuplicates()
    {
        await using var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();

        await using var sp = BuildProvider(container.GetConnectionString());
        await ApplyMigrationsAsync(sp);

        var now = DateTimeOffset.UtcNow;
        var app1 = await SeedAppointmentAsync(sp, now.AddDays(4));
        var app2 = await SeedAppointmentAsync(sp, now.AddDays(4));

        Guid action1Id;
        Guid action2Id;
        using (var seedScope = sp.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var a1 = AppointmentScheduledAction.Create(
                RiskThresholds.DefaultTenantId, app1, ScheduledActionKind.Reminder1, now.AddMinutes(-5), Guid.NewGuid(), utcNow: now.AddMinutes(-10));
            var a2 = AppointmentScheduledAction.Create(
                RiskThresholds.DefaultTenantId, app2, ScheduledActionKind.Reminder1, now.AddMinutes(-3), Guid.NewGuid(), utcNow: now.AddMinutes(-10));
            db.AppointmentScheduledActions.AddRange(a1, a2);
            await db.SaveChangesAsync();
            action1Id = a1.Id;
            action2Id = a2.Id;
        }

        using var barrier = new Barrier(2);
        var worker1Task = Task.Run(async () =>
        {
            using var scope = sp.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IAppointmentScheduledActionRepository>();
            barrier.SignalAndWait();
            return await repo.ClaimDueBatchAsync("pg-worker-1", now, TimeSpan.FromMinutes(2), batchSize: 1);
        });

        var worker2Task = Task.Run(async () =>
        {
            using var scope = sp.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IAppointmentScheduledActionRepository>();
            barrier.SignalAndWait();
            return await repo.ClaimDueBatchAsync("pg-worker-2", now, TimeSpan.FromMinutes(2), batchSize: 1);
        });

        var results = await Task.WhenAll(worker1Task, worker2Task);
        var allClaimedIds = results.SelectMany(r => r).Select(a => a.Id).ToList();

        allClaimedIds.Should().HaveCount(2);
        allClaimedIds.Distinct().Should().BeEquivalentTo([action1Id, action2Id]);
    }

    [DockerFact]
    public async Task ClaimDueBatchAsync_WhenLeaseExpires_ReclaimsActionAndFencesStaleWorkerOnPostgres()
    {
        await using var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();

        await using var sp = BuildProvider(container.GetConnectionString());
        await ApplyMigrationsAsync(sp);

        var t0 = DateTimeOffset.UtcNow;
        var appointmentId = await SeedAppointmentAsync(sp, t0.AddDays(4));

        Guid actionId;
        using (var seedScope = sp.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var action = AppointmentScheduledAction.Create(
                RiskThresholds.DefaultTenantId, appointmentId, ScheduledActionKind.Reminder1, t0.AddMinutes(-1), Guid.NewGuid(), utcNow: t0.AddMinutes(-5));
            db.AppointmentScheduledActions.Add(action);
            await db.SaveChangesAsync();
            actionId = action.Id;
        }

        // Worker 1 reclama con lease corto y se "cae"
        AppointmentScheduledAction staleClaim;
        using (var w1Scope = sp.CreateScope())
        {
            var repo1 = w1Scope.ServiceProvider.GetRequiredService<IAppointmentScheduledActionRepository>();
            var claimed1 = await repo1.ClaimDueBatchAsync("worker-1", t0, TimeSpan.FromSeconds(5), batchSize: 10);
            claimed1.Should().ContainSingle();
            staleClaim = claimed1[0];
        }

        // Worker 1 había cargado la fila con su LockToken original antes de quedarse dormido
        using var staleScope = sp.CreateScope();
        var staleDb = staleScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var staleTracked = await staleDb.AppointmentScheduledActions.FirstAsync(a => a.Id == actionId);

        // Worker 2 reclama tras vencer el lease (+10s)
        using (var w2Scope = sp.CreateScope())
        {
            var repo2 = w2Scope.ServiceProvider.GetRequiredService<IAppointmentScheduledActionRepository>();
            var db2 = w2Scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var claimed2 = await repo2.ClaimDueBatchAsync("worker-2", t0.AddSeconds(10), TimeSpan.FromMinutes(2), batchSize: 10);
            claimed2.Should().ContainSingle();
            claimed2[0].Attempts.Should().Be(2);
            claimed2[0].LockToken.Should().NotBeNull();
            claimed2[0].LockToken!.Value.Should().NotBe(staleClaim.LockToken!.Value);

            var tracked2 = await db2.AppointmentScheduledActions.FirstAsync(a => a.Id == actionId);
            tracked2.MarkCompleted(t0.AddSeconds(11), "{\"recovered\":true}");
            await db2.SaveChangesAsync();
        }

        // Si el Worker 1 despierta e intenta guardar con su LockToken antiguo, EF Core lanza DbUpdateConcurrencyException
        staleTracked.MarkSkipped("Stale write attempt", t0.AddSeconds(12));
        Func<Task> staleCommit = () => staleDb.SaveChangesAsync();
        await staleCommit.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [DockerFact]
    public async Task PartialUniqueIndex_RejectsDuplicateActiveKind_AndAllowsNewPlanAfterCancellation()
    {
        await using var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();

        await using var sp = BuildProvider(container.GetConnectionString());
        await ApplyMigrationsAsync(sp);

        var t0 = DateTimeOffset.UtcNow;
        var appointmentId = await SeedAppointmentAsync(sp, t0.AddDays(4));

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var first = AppointmentScheduledAction.Create(
                RiskThresholds.DefaultTenantId, appointmentId, ScheduledActionKind.Reminder1, t0.AddHours(10), Guid.NewGuid(), planVersion: 1, utcNow: t0);
            db.AppointmentScheduledActions.Add(first);
            await db.SaveChangesAsync();

            var duplicate = AppointmentScheduledAction.Create(
                RiskThresholds.DefaultTenantId, appointmentId, ScheduledActionKind.Reminder1, t0.AddHours(12), Guid.NewGuid(), planVersion: 2, utcNow: t0);
            db.AppointmentScheduledActions.Add(duplicate);

            Func<Task> act = () => db.SaveChangesAsync();
            var ex = await act.Should().ThrowAsync<DbUpdateException>();
            ex.Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var existing = await db.AppointmentScheduledActions.FirstAsync(a => a.AppointmentId == appointmentId);
            existing.Cancel("AppointmentRescheduled", t0.AddMinutes(5));

            var replacement = AppointmentScheduledAction.Create(
                RiskThresholds.DefaultTenantId, appointmentId, ScheduledActionKind.Reminder1, t0.AddHours(24), Guid.NewGuid(), planVersion: 2, utcNow: t0.AddMinutes(5));
            db.AppointmentScheduledActions.Add(replacement);
            await db.SaveChangesAsync();

            var actions = await db.AppointmentScheduledActions
                .AsNoTracking()
                .Where(a => a.AppointmentId == appointmentId)
                .OrderBy(a => a.PlanVersion)
                .ToListAsync();

            actions.Should().HaveCount(2);
            actions[0].Status.Should().Be(ScheduledActionStatus.Cancelled);
            actions[1].Status.Should().Be(ScheduledActionStatus.Pending);
        }
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IAppointmentScheduledActionRepository, AppointmentScheduledActionRepository>();
        services.AddScoped<ITenantConfirmationPolicyRepository, TenantConfirmationPolicyRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<IScheduleBlockRepository, ScheduleBlockRepository>();
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services.BuildServiceProvider();
    }

    private static async Task ApplyMigrationsAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    }

    private static async Task<Guid> SeedAppointmentAsync(ServiceProvider sp, DateTimeOffset startUtc)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@pg.cl", UserRole.Medico);
        var patUser = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@pg.cl", UserRole.Paciente);
        var staff = Staff.Create(staffUser.Id, "Dra", "Vega", $"{Random.Shared.Next(10000000, 99999999)}-1", "Medicina General", "Medico", "+56911111111");
        var patient = Patient.Create(patUser.Id, $"{Random.Shared.Next(10000000, 99999999)}-2", "Luis", "Mora", new DateOnly(1985, 4, 12), "M", "+56922222222", email: $"luis_{Guid.NewGuid():N}@pg.cl");
        var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "1");
        var start = startUtc.UtcDateTime;
        var block = ScheduleBlock.Create(staff.Id, box.Id, start, start.AddMinutes(30), Guid.NewGuid(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(start), new TimeOnly(10, 0), new TimeOnly(10, 30), [start.DayOfWeek]);
        var appointment = Appointment.Create(block.Id, patient.Id, "Control", "paciente");

        db.Users.AddRange(staffUser, patUser);
        db.Staffs.Add(staff);
        db.Patients.Add(patient);
        db.Boxes.Add(box);
        db.ScheduleBlocks.Add(block);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return appointment.Id;
    }
}
