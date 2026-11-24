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

public class MetaWhatsAppNotificationChannelTests
{
    [Fact]
    public async Task SendAsync_When200Returned_BuildsGraphApiTemplatePayloadWithButtonsAndParsesWamid()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            capturedRequest = req;
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"messaging_product\":\"whatsapp\",\"messages\":[{\"id\":\"wamid.HBgNNTY5MTIzNDU2NzgVAgARGBI\"}]}",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var appointmentId = Guid.NewGuid();
        var channel = CreateChannel(handler);
        var message = CreateSampleMessage(
            recipient: "+56 9 1234 5678",
            templateName: "medcare_recordatorio_cita_v1",
            orderedParams: ["Camila", "Clínica MedCare", "26-11-2026", "10:00 hrs", "Cardiología"],
            buttons:
            [
                new NotificationActionButton($"CONFIRM:{appointmentId}", "Confirmar"),
                new NotificationActionButton($"CANCEL:{appointmentId}", "Cancelar")
            ]);

        var result = await channel.SendAsync(message);

        result.IsSuccess.Should().BeTrue();
        result.ProviderMessageId.Should().Be("wamid.HBgNNTY5MTIzNDU2NzgVAgARGBI");
        result.ProviderName.Should().Be("MetaCloud");

        capturedRequest!.RequestUri!.ToString().Should().Be("https://graph.facebook.com/v21.0/1234567890/messages");
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("meta-test-access-token");

        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("messaging_product").GetString().Should().Be("whatsapp");
        doc.RootElement.GetProperty("to").GetString().Should().Be("56912345678");
        doc.RootElement.GetProperty("type").GetString().Should().Be("template");

        var template = doc.RootElement.GetProperty("template");
        template.GetProperty("name").GetString().Should().Be("medcare_recordatorio_cita_v1");
        template.GetProperty("language").GetProperty("code").GetString().Should().Be("es");

        var components = template.GetProperty("components");
        components.GetArrayLength().Should().Be(3); // body + 2 quick reply buttons
        components[0].GetProperty("type").GetString().Should().Be("body");
        components[0].GetProperty("parameters").GetArrayLength().Should().Be(5);
        components[1].GetProperty("sub_type").GetString().Should().Be("quick_reply");
        components[1].GetProperty("index").GetString().Should().Be("0");
        components[1].GetProperty("parameters")[0].GetProperty("payload").GetString().Should().Be($"CONFIRM:{appointmentId}");
        components[2].GetProperty("sub_type").GetString().Should().Be("quick_reply");
        components[2].GetProperty("index").GetString().Should().Be("1");
    }

    [Fact]
    public async Task SendAsync_ForRescheduleHelp_SendsDynamicUrlButtonComponent()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"messages\":[{\"id\":\"wamid.resched.1\"}]}", Encoding.UTF8, "application/json")
            };
        });

        var message = CreateSampleMessage(
            recipient: "912345678",
            templateName: "medcare_ayuda_reagendamiento_v1",
            orderedParams: ["Camila", "26-11-2026", "10:00 hrs", "Clínica MedCare"],
            buttons: [new NotificationActionButton("RESCHEDULE", "Reagendar hora", "?token=tok_abc")]);

        var result = await CreateChannel(handler).SendAsync(message);
        result.IsSuccess.Should().BeTrue();

        using var doc = JsonDocument.Parse(capturedBody!);
        var urlBtn = doc.RootElement.GetProperty("template").GetProperty("components")[1];
        urlBtn.GetProperty("sub_type").GetString().Should().Be("url");
        urlBtn.GetProperty("index").GetString().Should().Be("0");
        urlBtn.GetProperty("parameters")[0].GetProperty("text").GetString().Should().Be("?token=tok_abc");
    }

    [Theory]
    [InlineData(131026, DeliveryFailureKind.InvalidRecipient, true)]
    [InlineData(131030, DeliveryFailureKind.InvalidRecipient, true)]
    [InlineData(132001, DeliveryFailureKind.Permanent, true)]
    [InlineData(131056, DeliveryFailureKind.RateLimited, false)]
    public async Task SendAsync_WhenMetaReturnsErrorCode_ClassifiesFailureAndFallbackFlag(
        int metaCode,
        DeliveryFailureKind expectedKind,
        bool expectedImmediateFallback)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    $"{{\"error\":{{\"message\":\"Meta error\",\"code\":{metaCode}}}}}",
                    Encoding.UTF8,
                    "application/json")
            }));

        var result = await CreateChannel(handler).SendAsync(CreateSampleMessage("+56911111111", "medcare_cita_agendada_v1", ["Ana"], []));

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(expectedKind);
        result.ErrorCode.Should().Be(metaCode.ToString());
        result.ShouldFallbackImmediately.Should().Be(expectedImmediateFallback);
    }

    [Fact]
    public async Task SendAsync_When503OrTimeout_ReturnsTransient()
    {
        var handler503 = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Service Unavailable")
            }));

        var res503 = await CreateChannel(handler503).SendAsync(CreateSampleMessage("+56911111111", "medcare_cita_agendada_v1", ["Ana"], []));
        res503.IsSuccess.Should().BeFalse();
        res503.FailureKind.Should().Be(DeliveryFailureKind.Transient);

        var timeoutHandler = new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var resTimeout = await CreateChannel(timeoutHandler, timeoutSeconds: 1)
            .SendAsync(CreateSampleMessage("+56911111111", "medcare_cita_agendada_v1", ["Ana"], []));
        resTimeout.IsSuccess.Should().BeFalse();
        resTimeout.FailureKind.Should().Be(DeliveryFailureKind.Transient);
        resTimeout.HttpStatusCode.Should().Be(408);
    }

    private static MetaWhatsAppNotificationChannel CreateChannel(StubHttpMessageHandler handler, int timeoutSeconds = 15)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.facebook.com/")
        };

        var options = Options.Create(new MetaWhatsAppOptions
        {
            BaseUrl = "https://graph.facebook.com/",
            ApiVersion = "v21.0",
            PhoneNumberId = "1234567890",
            AccessToken = "meta-test-access-token",
            DefaultTemplateLanguage = "es",
            TimeoutSeconds = timeoutSeconds
        });

        return new MetaWhatsAppNotificationChannel(httpClient, options, NullLogger<MetaWhatsAppNotificationChannel>.Instance);
    }

    private static NotificationMessage CreateSampleMessage(
        string recipient,
        string templateName,
        IReadOnlyList<string> orderedParams,
        IReadOnlyList<NotificationActionButton> buttons) => new(
        NotificationId: Guid.NewGuid(),
        TenantId: RiskThresholds.DefaultTenantId,
        AppointmentId: Guid.NewGuid(),
        PatientId: Guid.NewGuid(),
        Type: NotificationType.Reminder,
        Channel: NotificationChannel.WhatsApp,
        RecipientAddress: recipient,
        TemplateKey: "appointment.reminder",
        TemplateVersion: 1,
        Locale: "es-CL",
        Parameters: new Dictionary<string, string>(),
        Rendered: new RenderedNotification(
            Subject: null,
            PlainTextBody: "Recordatorio...",
            HtmlBody: null,
            SanitizedContentForStorage: "Recordatorio...",
            MetaTemplateName: templateName,
            MetaOrderedBodyParameters: orderedParams,
            Buttons: buttons),
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
