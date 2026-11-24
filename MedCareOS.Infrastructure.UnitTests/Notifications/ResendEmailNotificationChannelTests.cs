using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Configuration;
using MedCareOS.Infrastructure.Notifications.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedCareOS.Infrastructure.UnitTests.Notifications;

public class ResendEmailNotificationChannelTests
{
    [Fact]
    public async Task SendAsync_When200Returned_SendsExpectedHeadersPayloadAndParsesMessageId()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            capturedRequest = req;
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"re_msg_123456\"}", Encoding.UTF8, "application/json")
            };
        });

        var channel = CreateChannel(handler);
        var message = CreateSampleMessage();

        var result = await channel.SendAsync(message, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.ProviderMessageId.Should().Be("re_msg_123456");
        result.ProviderName.Should().Be("Resend");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Be("https://api.resend.com/emails");
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("re_test_api_key");
        capturedRequest.Headers.GetValues("Idempotency-Key").Should().ContainSingle().Which.Should().Be(message.NotificationId.ToString("N"));

        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("from").GetString().Should().Be("Clínica MedCare <notificaciones@medcare.cl>");
        doc.RootElement.GetProperty("to")[0].GetString().Should().Be("paciente@test.cl");
        doc.RootElement.GetProperty("subject").GetString().Should().Be("Recordatorio de cita");
        doc.RootElement.GetProperty("text").GetString().Should().Contain("26-11-2026");
        doc.RootElement.GetProperty("html").GetString().Should().Contain("<html>");
    }

    [Fact]
    public async Task SendAsync_When429Returned_ReturnsRateLimited()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"message\":\"Rate limit exceeded\"}")
            }));

        var result = await CreateChannel(handler).SendAsync(CreateSampleMessage());

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(DeliveryFailureKind.RateLimited);
        result.HttpStatusCode.Should().Be(429);
        result.ShouldFallbackImmediately.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_When503OrTimeout_ReturnsTransient()
    {
        var handler503 = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Service Unavailable")
            }));

        var res503 = await CreateChannel(handler503).SendAsync(CreateSampleMessage());
        res503.IsSuccess.Should().BeFalse();
        res503.FailureKind.Should().Be(DeliveryFailureKind.Transient);
        res503.HttpStatusCode.Should().Be(503);

        var timeoutHandler = new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var resTimeout = await CreateChannel(timeoutHandler, timeoutSeconds: 1).SendAsync(CreateSampleMessage());
        resTimeout.IsSuccess.Should().BeFalse();
        resTimeout.FailureKind.Should().Be(DeliveryFailureKind.Transient);
        resTimeout.HttpStatusCode.Should().Be(408);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, DeliveryFailureKind.InvalidRecipient)]
    [InlineData(HttpStatusCode.BadRequest, DeliveryFailureKind.InvalidRecipient)]
    [InlineData(HttpStatusCode.Unauthorized, DeliveryFailureKind.Permanent)]
    [InlineData(HttpStatusCode.Forbidden, DeliveryFailureKind.Permanent)]
    public async Task SendAsync_When4xxReturned_ClassifiesFailureCorrectly(HttpStatusCode code, DeliveryFailureKind expectedKind)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent("{\"message\":\"error\"}")
            }));

        var result = await CreateChannel(handler).SendAsync(CreateSampleMessage());

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(expectedKind);
        result.HttpStatusCode.Should().Be((int)code);
    }

    [Fact]
    public async Task SendAsync_WhenApiKeyMissing_FailsClosedAsPermanent()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("No debería llamar HTTP"));
        var channel = CreateChannel(handler, apiKey: "");

        var result = await channel.SendAsync(CreateSampleMessage());

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(DeliveryFailureKind.Permanent);
    }

    private static ResendEmailNotificationChannel CreateChannel(
        StubHttpMessageHandler handler,
        string apiKey = "re_test_api_key",
        int timeoutSeconds = 15)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.resend.com/")
        };

        var options = Options.Create(new ResendOptions
        {
            BaseUrl = "https://api.resend.com/",
            ApiKey = apiKey,
            FromAddress = "notificaciones@medcare.cl",
            FromName = "Clínica MedCare",
            TimeoutSeconds = timeoutSeconds
        });

        return new ResendEmailNotificationChannel(httpClient, options, NullLogger<ResendEmailNotificationChannel>.Instance);
    }

    private static NotificationMessage CreateSampleMessage() => new(
        NotificationId: Guid.NewGuid(),
        TenantId: RiskThresholds.DefaultTenantId,
        AppointmentId: Guid.NewGuid(),
        PatientId: Guid.NewGuid(),
        Type: NotificationType.Reminder,
        Channel: NotificationChannel.Email,
        RecipientAddress: "paciente@test.cl",
        TemplateKey: "appointment.reminder",
        TemplateVersion: 1,
        Locale: "es-CL",
        Parameters: new Dictionary<string, string>(),
        Rendered: new RenderedNotification(
            Subject: "Recordatorio de cita",
            PlainTextBody: "Hola Camila, te recordamos tu cita el 26-11-2026 a las 10:00 hrs.",
            HtmlBody: "<html><body><p>Hola Camila</p></body></html>",
            SanitizedContentForStorage: "Hola Camila...",
            MetaTemplateName: null,
            MetaOrderedBodyParameters: [],
            Buttons: []),
        CorrelationId: Guid.NewGuid());

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _fn;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn)
            => _fn = fn;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _fn(request, cancellationToken);
    }
}
