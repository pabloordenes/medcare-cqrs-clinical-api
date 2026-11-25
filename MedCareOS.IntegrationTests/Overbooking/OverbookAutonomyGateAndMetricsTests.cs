using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.Json;
using FluentAssertions;
using MedCareOS.Application.Appointments.Commands.CheckInAppointment;
using MedCareOS.Application.Appointments.Commands.GenerateOverbook;
using MedCareOS.Application.Dashboard.Queries.GetMetrics;
using MedCareOS.Application.Overbooking.Commands.SeedSyntheticShadowDecisions;
using MedCareOS.Application.Overbooking.Commands.SetTenantAutoOverbook;
using MedCareOS.Application.Overbooking.Queries.GetOverbookAutonomyReadiness;
using MedCareOS.Application.Overbooking.Queries.GetOverbookMetrics;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Services;
using MedCareOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedCareOS.IntegrationTests.Overbooking;

public class OverbookAutonomyGateAndMetricsTests : IClassFixture<CustomApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly CustomApiFactory _factory;

    public OverbookAutonomyGateAndMetricsTests(CustomApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AutonomyEndpoints_ShouldEnforceAdminRbac_BlockBelow200_UnlockViaSyntheticSeed_AndWriteAuditLog()
    {
        var tenantId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Users.Add(User.Create(adminUserId, $"adm_{adminUserId:N}@test.cl", UserRole.Administrador));
            db.TenantConfirmationPolicies.Add(TenantConfirmationPolicy.CreateDefault(tenantId));
            await db.SaveChangesAsync();
        }

        // RBAC: Paciente y Recepcionista no pueden leer ni mutar el gate -> 403
        using var patientClient = CreateClient(Guid.NewGuid(), "Paciente");
        using var receptionClient = CreateClient(Guid.NewGuid(), "Recepcionista");
        using var adminClient = CreateClient(adminUserId, "Administrador");

        (await patientClient.GetAsync($"/api/admin/tenants/overbook-autonomy/readiness?tenantId={tenantId}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await receptionClient.PatchAsJsonAsync("/api/admin/tenants/overbook-autonomy", new
        {
            TenantId = tenantId,
            Enabled = true,
            Reason = "Intento no autorizado"
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Con 0 evaluaciones sombra -> readiness = false, activar sin override -> 422
        var readinessBeforeRes = await adminClient.GetAsync($"/api/admin/tenants/overbook-autonomy/readiness?tenantId={tenantId}");
        readinessBeforeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var readinessBefore = await readinessBeforeRes.Content.ReadFromJsonAsync<OverbookAutonomyReadinessDto>(JsonOptions);
        readinessBefore!.IsEligibleForAutonomy.Should().BeFalse();
        readinessBefore.AutoOverbookEnabled.Should().BeFalse();

        var blockedEnableRes = await adminClient.PatchAsJsonAsync("/api/admin/tenants/overbook-autonomy", new
        {
            TenantId = tenantId,
            Enabled = true,
            Reason = "Activación prematura"
        });
        blockedEnableRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Sembrar muestra sintética (>=200, FPR < 15%, is_synthetic=true)
        var seedRes = await adminClient.PostAsJsonAsync("/api/admin/tenants/overbook-autonomy/seed-synthetic", new
        {
            TenantId = tenantId,
            SampleSize = 220
        });
        seedRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var seedBody = await seedRes.Content.ReadFromJsonAsync<SeedSyntheticShadowDecisionsResult>(JsonOptions);
        seedBody!.IsEligibleForAutonomy.Should().BeTrue();
        seedBody.FalsePositiveRate.Should().BeLessThan(0.15m);
        seedBody.SeededCount.Should().Be(220);

        // Verificar readiness desbloqueado y marcado como sintético
        var readinessAfter = await (await adminClient.GetAsync($"/api/admin/tenants/overbook-autonomy/readiness?tenantId={tenantId}"))
            .Content.ReadFromJsonAsync<OverbookAutonomyReadinessDto>(JsonOptions);
        readinessAfter!.IsEligibleForAutonomy.Should().BeTrue();
        readinessAfter.IncludesSyntheticData.Should().BeTrue();

        // Habilitación explícita por Admin -> 200 OK y AuditLog persistido
        var enableRes = await adminClient.PatchAsJsonAsync("/api/admin/tenants/overbook-autonomy", new
        {
            TenantId = tenantId,
            Enabled = true,
            Reason = "Calibración validada en simulación bajo 15% FPR sobre 220 citas"
        });
        enableRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var enableBody = await enableRes.Content.ReadFromJsonAsync<SetTenantAutoOverbookResult>(JsonOptions);
        enableBody!.PreviousAutoOverbookEnabled.Should().BeFalse();
        enableBody.NewAutoOverbookEnabled.Should().BeTrue();
        enableBody.GatePassed.Should().BeTrue();
        enableBody.ForcedOverride.Should().BeFalse();

        var auditRes = await adminClient.GetAsync($"/api/admin/tenants/overbook-autonomy/audit?tenantId={tenantId}");
        auditRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditJson = await auditRes.Content.ReadFromJsonAsync<JsonElement>();
        auditJson.GetArrayLength().Should().Be(1);
        auditJson[0].GetProperty("changedByUserId").GetGuid().Should().Be(adminUserId);
    }

    [Fact]
    public async Task ShadowDecisionRecordingAndE48DashboardMetrics_ShouldTrackAllSixOverbookMetricsPerTenant()
    {
        var tenantId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        Guid shadowAppId, slotRefilledObAppId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var uAdmin = User.Create(adminUserId, $"met_adm_{adminUserId:N}@test.cl", UserRole.Administrador);
            var uOrig1 = User.Create(Guid.NewGuid(), $"met_o1_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var uOrig2 = User.Create(Guid.NewGuid(), $"met_o2_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var uOb = User.Create(Guid.NewGuid(), $"met_ob_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var uDoc = User.Create(Guid.NewGuid(), $"met_d_{Guid.NewGuid():N}@test.cl", UserRole.Medico);

            var pOrig1 = Patient.Create(uOrig1.Id, $"{Random.Shared.Next(10000000, 99999999)}-1", "Shadow", "FP", new DateOnly(1992, 1, 1), "F", "+56931000001", $"s_{Guid.NewGuid():N}@test.cl");
            var pOrig2 = Patient.Create(uOrig2.Id, $"{Random.Shared.Next(10000000, 99999999)}-2", "Orig", "Late", new DateOnly(1990, 2, 2), "M", "+56931000002", $"o_{Guid.NewGuid():N}@test.cl");
            var pOb = Patient.Create(uOb.Id, $"{Random.Shared.Next(10000000, 99999999)}-3", "Fill", "Attend", new DateOnly(1989, 3, 3), "M", "+56931000003", $"f_{Guid.NewGuid():N}@test.cl");
            var staff = Staff.Create(uDoc.Id, "Dr", "Metrics", $"{Random.Shared.Next(10000000, 99999999)}-4", "Medicina General", "Medico", "+56931000004");
            var box = Box.Create("Box Met", "Consulta", 2, "1");

            var policy = TenantConfirmationPolicy.CreateDefault(tenantId); // AutoOverbookEnabled = false por defecto

            var s1 = DateTime.UtcNow.AddDays(3);
            var s2 = DateTime.UtcNow.AddDays(4);
            var s3 = DateTime.UtcNow.AddDays(5);
            var b1 = ScheduleBlock.Create(staff.Id, box.Id, s1, s1.AddHours(1), Guid.NewGuid(), DateOnly.FromDateTime(s1), DateOnly.FromDateTime(s1), new TimeOnly(9, 0), new TimeOnly(10, 0), [s1.DayOfWeek]);
            var b2 = ScheduleBlock.Create(staff.Id, box.Id, s2, s2.AddHours(1), Guid.NewGuid(), DateOnly.FromDateTime(s2), DateOnly.FromDateTime(s2), new TimeOnly(10, 0), new TimeOnly(11, 0), [s2.DayOfWeek]);
            var b3 = ScheduleBlock.Create(staff.Id, box.Id, s3, s3.AddHours(1), Guid.NewGuid(), DateOnly.FromDateTime(s3), DateOnly.FromDateTime(s3), new TimeOnly(11, 0), new TimeOnly(12, 0), [s3.DayOfWeek]);

            // Cita 1: en modo sombra, high risk, Delivered, sin respuesta -> GenerateOverbook rechazado en modo autónomo registra OverbookShadowDecision real
            var appShadow = Appointment.Create(b1.Id, pOrig1.Id, "Control general", tenantId: tenantId);
            appShadow.UpdateRiskScore(0.91m, "ALTO");
            appShadow.MarkFinalConfirmationRequested(DateTimeOffset.UtcNow.AddHours(-2));
            appShadow.MarkUnresponsive(DateTimeOffset.UtcNow.AddMinutes(-20));
            var finalNotif = Notification.Create(tenantId, appShadow.Id, pOrig1.Id, NotificationType.FinalConfirmation, NotificationChannel.Email, "appointment.final_confirmation", 1, "es-CL", "{}", DateTimeOffset.UtcNow.AddHours(-2), Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-2));
            finalNotif.MarkSent("fake", "msg_met_1", "Confirmación final", DateTimeOffset.UtcNow.AddHours(-2));
            finalNotif.ApplyDeliveryStatus(NotificationStatus.Delivered, DateTimeOffset.UtcNow.AddHours(-1), null);

            // Cita 2: sobrecupo ocupado y atendido (SlotRefilled = 1) + original desplazada y confirmó tarde + 1 collision event
            var appOrig2 = Appointment.Create(b2.Id, pOrig2.Id, "Control", tenantId: tenantId);
            appOrig2.MarkFinalConfirmationRequested(DateTimeOffset.UtcNow.AddHours(-3));
            appOrig2.MarkUnresponsive(DateTimeOffset.UtcNow.AddMinutes(-40));
            appOrig2.RegisterPatientConfirmation(DateTimeOffset.UtcNow.AddMinutes(-5));
            appOrig2.MarkDisplacedByOverbook(DateTimeOffset.UtcNow.AddMinutes(-4));

            var ob2 = ScheduleBlock.CreateOverbook(b2, appOrig2.Id);
            var appOb2 = Appointment.CreateFromWaitingListOffer(ob2.Id, pOb.Id, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-10), "Backfill", tenantId: tenantId);
            appOb2.CheckIn(DateTimeOffset.UtcNow);
            ob2.MarkOverbookOccupied(appOb2.Id, DateTimeOffset.UtcNow.AddMinutes(-10));

            var collision = OverbookCollisionEvent.CreateCollision(
                tenantId, appOrig2.Id, appOb2.Id, ob2.Id, "LateConfirmation",
                new OverbookPriorityDecision(OverbookWinner.Overbook, appOb2.Id, appOrig2.Id, "EARLIER_CONFIRMATION_INSTANT", "Prioridad sobrecupo"),
                DateTimeOffset.UtcNow.AddMinutes(-4));

            // Cita 3: sobrecupo desactivado por Undo (OverbookUndone = 1)
            var appOrig3 = Appointment.Create(b3.Id, pOrig2.Id, "Control 3", tenantId: tenantId);
            var ob3 = ScheduleBlock.CreateOverbook(b3, appOrig3.Id);
            ob3.DeactivateOverbook("LateConfirmationUndo", DateTimeOffset.UtcNow);

            db.Users.AddRange(uAdmin, uOrig1, uOrig2, uOb, uDoc);
            db.Patients.AddRange(pOrig1, pOrig2, pOb);
            db.Staffs.Add(staff);
            db.Boxes.Add(box);
            db.TenantConfirmationPolicies.Add(policy);
            db.ScheduleBlocks.AddRange(b1, b2, b3, ob2, ob3);
            db.Appointments.AddRange(appShadow, appOrig2, appOb2, appOrig3);
            db.Notifications.Add(finalNotif);
            db.OverbookCollisionEvents.Add(collision);
            await db.SaveChangesAsync();

            shadowAppId = appShadow.Id;
            slotRefilledObAppId = appOb2.Id;

            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var obAttempt = await sender.Send(new GenerateOverbookCommand(shadowAppId, DecisionMode: OverbookDecisionMode.Autonomous, TargetRisk: 0.91m));
            obAttempt.ErrorCode.Should().Be("AUTONOMOUS_MODE_DISABLED");

            // Paciente de cita 1 hace CheckIn sin haber confirmado -> ShadowDecision resuelta como Falso Positivo!
            await sender.Send(new CheckInAppointmentCommand(shadowAppId));
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var shadowRow = await db.OverbookShadowDecisions.AsNoTracking().SingleAsync(d => d.AppointmentId == shadowAppId);
            shadowRow.WouldHaveOverbooked.Should().BeTrue();
            shadowRow.IsSynthetic.Should().BeFalse();
            shadowRow.OutcomeAttendance.Should().BeTrue();
            shadowRow.IsFalsePositive.Should().BeTrue();
        }

        using var authClient = CreateClient(adminUserId, "Administrador");

        var metricsRes = await authClient.GetAsync($"/api/dashboard/overbook-metrics?tenantId={tenantId}");
        metricsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var metrics = await metricsRes.Content.ReadFromJsonAsync<OverbookMetricsDto>(JsonOptions);

        metrics.Should().NotBeNull();
        metrics!.SlotRefilled.Should().Be(1);
        metrics.OverbookSlotsOpened.Should().Be(2);
        metrics.OverbookSlotsOccupied.Should().Be(1);
        metrics.OverbookCollisions.Should().Be(1);
        metrics.DisplacedPatients.Should().Be(1);
        metrics.ConfirmedLateAfterOverbook.Should().Be(1);
        metrics.OverbookUndone.Should().Be(1);
        metrics.ShadowEvaluatedCount.Should().Be(1);
        metrics.ShadowFalsePositivesCount.Should().Be(1);
        metrics.FalsePositiveRate.Should().Be(1.0000m);
        metrics.IncludesSyntheticData.Should().BeFalse();

        // También expuesto en GET /api/dashboard/metrics (E7)
        var dashRes = await authClient.GetAsync("/api/dashboard/metrics");
        dashRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var dashDto = await dashRes.Content.ReadFromJsonAsync<DashboardAusentismoDto>(JsonOptions);
        dashDto!.OverbookMetrics.Should().NotBeNull();
    }

    private HttpClient CreateClient(Guid userId, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }
}
