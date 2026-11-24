using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MassTransit.Testing;
using MedCareOS.Application.Notifications.Events;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedCareOS.IntegrationTests.Notifications;

public class ProviderWebhooksIntegrationTests : IClassFixture<CustomApiFactory>
{
    private const string TestMetaAppSecret = "test-meta-app-secret-123456";
    private const string TestMetaVerifyToken = "test-meta-verify-token-123456";
    private const string TestResendSecret = "whsec_dGVzdC1yZXNlbmQtc3ZpeC1zaWduaW5nLWtleS0zMmI=";

    private readonly CustomApiFactory _factory;

    public ProviderWebhooksIntegrationTests(CustomApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MetaVerificationChallenge_ValidTokenReturns200Challenge_InvalidReturns403()
    {
        using var client = _factory.CreateClient();

        var validRes = await client.GetAsync(
            $"/api/webhooks/notifications/whatsapp?hub.mode=subscribe&hub.verify_token={TestMetaVerifyToken}&hub.challenge=challenge_12345");
        validRes.StatusCode.Should().Be(HttpStatusCode.OK);
        (await validRes.Content.ReadAsStringAsync()).Should().Be("challenge_12345");

        var invalidRes = await client.GetAsync(
            "/api/webhooks/notifications/whatsapp?hub.mode=subscribe&hub.verify_token=wrong_token&hub.challenge=challenge_12345");
        invalidRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MetaWebhook_InvalidSignatureReturns401_AndValidDeliveryStatusesUpdateMonotonically()
    {
        var (_, _, notificationId) = await SeedNotificationAsync(
            NotificationChannel.WhatsApp,
            "wamid.status_test_1",
            "+56988887777");

        using var client = _factory.CreateClient();

        var readPayload = BuildMetaStatusPayload("wamid.status_test_1", "read", 1732450100);
        var deliveredPayload = BuildMetaStatusPayload("wamid.status_test_1", "delivered", 1732450050);

        // Invalid signature -> 401
        var badReq = CreateMetaSignedRequest("/api/webhooks/notifications/whatsapp", readPayload, "wrong-secret");
        var badRes = await client.SendAsync(badReq);
        badRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Read arrives first -> status becomes Read
        var readRes1 = await client.SendAsync(CreateMetaSignedRequest("/api/webhooks/notifications/whatsapp", readPayload, TestMetaAppSecret));
        readRes1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Duplicate read replay -> idempotent 200
        var readRes2 = await client.SendAsync(CreateMetaSignedRequest("/api/webhooks/notifications/whatsapp", readPayload, TestMetaAppSecret));
        readRes2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Out-of-order Delivered arrives after Read -> status stays Read (monotonic)
        var deliveredRes = await client.SendAsync(CreateMetaSignedRequest("/api/webhooks/notifications/whatsapp", deliveredPayload, TestMetaAppSecret));
        deliveredRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.Notifications.AsNoTracking().FirstAsync(n => n.Id == notificationId);

        saved.Status.Should().Be(NotificationStatus.Read);
        saved.ReadAt.Should().NotBeNull();
        saved.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MetaWebhook_QuickReplyConfirmAndTextCancelAndStop_ShouldUpdateAppointmentAndOptOut()
    {
        var phone1 = $"+569{Random.Shared.Next(10000000, 99999999)}";
        var phone2 = $"+569{Random.Shared.Next(10000000, 99999999)}";

        var (appConfirmId, patConfirmId, _) = await SeedNotificationAsync(
            NotificationChannel.WhatsApp,
            $"wamid.out.{Guid.NewGuid():N}",
            phone1);

        var (appCancelId, patCancelId, _) = await SeedNotificationAsync(
            NotificationChannel.WhatsApp,
            $"wamid.out.{Guid.NewGuid():N}",
            phone2);

        using var client = _factory.CreateClient();

        // 1. Quick Reply CONFIRM:<appointmentId> confirms appointment #1
        var confirmJson = BuildMetaInboundButtonPayload(
            $"wamid.in.{Guid.NewGuid():N}",
            phone1.TrimStart('+'),
            $"CONFIRM:{appConfirmId}",
            "Confirmar");
        var confirmRes = await client.SendAsync(
            CreateMetaSignedRequest("/api/webhooks/notifications/whatsapp", confirmJson, TestMetaAppSecret));
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Free text "NO" cancels appointment #2 via latest actionable notification
        var cancelJson = BuildMetaInboundTextPayload($"wamid.in.{Guid.NewGuid():N}", phone2.TrimStart('+'), "NO");
        var cancelRes = await client.SendAsync(
            CreateMetaSignedRequest("/api/webhooks/notifications/whatsapp", cancelJson, TestMetaAppSecret));
        cancelRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Free text "STOP" opts out patient #1
        var stopJson = BuildMetaInboundTextPayload($"wamid.in.{Guid.NewGuid():N}", phone1.TrimStart('+'), "STOP");
        var stopRes = await client.SendAsync(
            CreateMetaSignedRequest("/api/webhooks/notifications/whatsapp", stopJson, TestMetaAppSecret));
        stopRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var confirmedApp = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appConfirmId);
            var cancelledApp = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appCancelId);
            var optedOutPat = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == patConfirmId);
            var activePat = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == patCancelId);

            confirmedApp.Status.Should().Be(AppointmentStatus.Confirmed);
            cancelledApp.Status.Should().Be(AppointmentStatus.Cancelled);
            optedOutPat.OptOutAt.Should().NotBeNull();
            optedOutPat.OptOutSource.Should().Be("whatsapp_stop");
            optedOutPat.WhatsAppOptIn.Should().BeFalse();
            activePat.OptOutAt.Should().BeNull();
        }

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        (await harness.Published.Any<PatientAppointmentReplyReceived>(
            p => p.Context.Message.AppointmentId == appConfirmId && p.Context.Message.Intent == PatientReplyIntent.Confirm))
            .Should().BeTrue();
        (await harness.Published.Any<PatientAppointmentReplyReceived>(
            p => p.Context.Message.AppointmentId == appCancelId && p.Context.Message.Intent == PatientReplyIntent.Cancel))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ResendEmailWebhook_ValidSvixUpdatesStatusAndComplaintTriggersOptOut_InvalidReturns401()
    {
        var emailId1 = $"re_{Guid.NewGuid():N}";
        var emailId2 = $"re_{Guid.NewGuid():N}";

        var (_, pat1Id, notif1Id) = await SeedNotificationAsync(NotificationChannel.Email, emailId1, "+56911112222");
        var (_, pat2Id, notif2Id) = await SeedNotificationAsync(NotificationChannel.Email, emailId2, "+56933334444");

        using var client = _factory.CreateClient();

        // Invalid signature -> 401
        var deliveredBody = $"{{\"type\":\"email.delivered\",\"created_at\":\"2026-11-24T15:00:00Z\",\"data\":{{\"email_id\":\"{emailId1}\"}}}}";
        var invalidReq = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/notifications/email")
        {
            Content = new StringContent(deliveredBody, Encoding.UTF8, "application/json")
        };
        invalidReq.Headers.Add("svix-id", $"msg_{Guid.NewGuid():N}");
        invalidReq.Headers.Add("svix-timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        invalidReq.Headers.Add("svix-signature", "v1,aW52YWxpZA==");
        (await client.SendAsync(invalidReq)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Valid email.delivered -> Delivered
        var validDelivered = CreateSvixSignedRequest("/api/webhooks/notifications/email", deliveredBody, TestResendSecret);
        (await client.SendAsync(validDelivered)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Valid email.complained -> Failed + Patient OptOut
        var complainedBody = $"{{\"type\":\"email.complained\",\"created_at\":\"2026-11-24T15:05:00Z\",\"data\":{{\"email_id\":\"{emailId2}\"}}}}";
        var validComplained = CreateSvixSignedRequest("/api/webhooks/notifications/email", complainedBody, TestResendSecret);
        (await client.SendAsync(validComplained)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var n1 = await db.Notifications.AsNoTracking().FirstAsync(n => n.Id == notif1Id);
        var n2 = await db.Notifications.AsNoTracking().FirstAsync(n => n.Id == notif2Id);
        var p1 = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == pat1Id);
        var p2 = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == pat2Id);

        n1.Status.Should().Be(NotificationStatus.Delivered);
        p1.OptOutAt.Should().BeNull();

        n2.Status.Should().Be(NotificationStatus.Failed);
        p2.OptOutAt.Should().NotBeNull();
        p2.OptOutSource.Should().Be("email_complaint");
    }

    [Fact]
    public async Task ProviderWebhooks_WhenRateLimitExceeded_ShouldReturn429()
    {
        using var limitedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Notifications:RateLimiting:ProviderWebhooksPermitLimit", "2");
            builder.UseSetting("Notifications:RateLimiting:ProviderWebhooksWindowSeconds", "60");
        });

        using var client = limitedFactory.CreateClient();
        var url = $"/api/webhooks/notifications/whatsapp?hub.mode=subscribe&hub.verify_token={TestMetaVerifyToken}&hub.challenge=ok";

        var r1 = await client.GetAsync(url);
        var r2 = await client.GetAsync(url);
        var r3 = await client.GetAsync(url);

        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        r3.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private async Task<(Guid AppointmentId, Guid PatientId, Guid NotificationId)> SeedNotificationAsync(
        NotificationChannel channel,
        string providerMessageId,
        string phone)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
        var patUser = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
        var staff = Staff.Create(staffUser.Id, "Dr", "Reyes", $"{Random.Shared.Next(10000000, 99999999)}-1", "Medicina General", "Medico", "+56911111111");
        var patient = Patient.Create(
            patUser.Id,
            $"{Random.Shared.Next(10000000, 99999999)}-2",
            "Sofia",
            "Castro",
            new DateOnly(1993, 2, 10),
            "femenino",
            phone,
            email: $"sofia_{Guid.NewGuid():N}@test.cl",
            preferredChannel: channel,
            whatsAppOptIn: channel == NotificationChannel.WhatsApp,
            consentSource: "test_seed");
        var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "1");
        var start = DateTime.UtcNow.AddDays(2);
        var block = ScheduleBlock.Create(staff.Id, box.Id, start, start.AddMinutes(30), Guid.NewGuid(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(start), new TimeOnly(10, 0), new TimeOnly(10, 30), [start.DayOfWeek]);
        var appointment = Appointment.Create(block.Id, patient.Id, "Control", "paciente");

        var now = DateTimeOffset.UtcNow;
        var notification = Notification.Create(
            RiskThresholds.DefaultTenantId,
            appointment.Id,
            patient.Id,
            NotificationType.Reminder,
            channel,
            "appointment.reminder",
            1,
            "es-CL",
            "{}",
            now,
            Guid.NewGuid(),
            now);
        notification.MarkSent(channel == NotificationChannel.WhatsApp ? "MetaCloud" : "Resend", providerMessageId, "Recordatorio...", now);

        db.Users.AddRange(staffUser, patUser);
        db.Staffs.Add(staff);
        db.Patients.Add(patient);
        db.Boxes.Add(box);
        db.ScheduleBlocks.Add(block);
        db.Appointments.Add(appointment);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        return (appointment.Id, patient.Id, notification.Id);
    }

    private static HttpRequestMessage CreateMetaSignedRequest(string uri, string jsonBody, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(jsonBody);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = "sha256=" + Convert.ToHexString(hmac.ComputeHash(bytes)).ToLowerInvariant();

        var req = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(bytes)
        };
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        req.Headers.Add("X-Hub-Signature-256", sig);
        return req;
    }

    private static HttpRequestMessage CreateSvixSignedRequest(string uri, string jsonBody, string whsecSecret)
    {
        var bytes = Encoding.UTF8.GetBytes(jsonBody);
        var svixId = $"msg_{Guid.NewGuid():N}";
        var svixTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var keyBytes = Convert.FromBase64String(whsecSecret.Replace("whsec_", "", StringComparison.Ordinal));
        var signedPayload = $"{svixId}.{svixTs}.{jsonBody}";

        using var hmac = new HMACSHA256(keyBytes);
        var sig = "v1," + Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload)));

        var req = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(bytes)
        };
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        req.Headers.Add("svix-id", svixId);
        req.Headers.Add("svix-timestamp", svixTs);
        req.Headers.Add("svix-signature", sig);
        return req;
    }

    private static string BuildMetaStatusPayload(string wamid, string status, long unixTs) => $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [
            {
              "id": "waba_1",
              "changes": [
                {
                  "field": "messages",
                  "value": {
                    "statuses": [
                      {
                        "id": "{{wamid}}",
                        "status": "{{status}}",
                        "timestamp": "{{unixTs}}"
                      }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string BuildMetaInboundButtonPayload(string msgId, string fromDigits, string payload, string title) => $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [
            {
              "id": "waba_1",
              "changes": [
                {
                  "field": "messages",
                  "value": {
                    "messages": [
                      {
                        "from": "{{fromDigits}}",
                        "id": "{{msgId}}",
                        "timestamp": "1732450200",
                        "type": "button",
                        "button": {
                          "payload": "{{payload}}",
                          "text": "{{title}}"
                        }
                      }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string BuildMetaInboundTextPayload(string msgId, string fromDigits, string textBody) => $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [
            {
              "id": "waba_1",
              "changes": [
                {
                  "field": "messages",
                  "value": {
                    "messages": [
                      {
                        "from": "{{fromDigits}}",
                        "id": "{{msgId}}",
                        "timestamp": "1732450300",
                        "type": "text",
                        "text": {
                          "body": "{{textBody}}"
                        }
                      }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;
}
