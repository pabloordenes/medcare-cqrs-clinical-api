using System.Net;
using System.Text;
using FluentAssertions;
using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Infrastructure.Configuration;
using MedCareOS.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace MedCareOS.Infrastructure.UnitTests.Services;

public class FastApiClinicalServiceTests
{
    [Fact]
    public async Task PredictNoShowRiskAsync_When503Returned_ThrowsAiServiceTransientException_NeverFallsBackToZero()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Service Unavailable")
            });

        var service = CreateService(handler);
        var request = new PredictRequestDto(Guid.NewGuid(), 30, "femenino", 3, "medicina_general", 0, "ninguno");

        Func<Task> act = () => service.PredictNoShowRiskAsync(request, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AiServiceTransientException>();
        ex.Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task GetNlpSummaryAsync_WhenTimeoutOccurs_ThrowsAiServiceTransientException_NeverCopiesSymptomsRaw()
    {
        var handler = new StubHttpMessageHandler(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var service = CreateService(handler, timeoutSeconds: 1);

        Func<Task> act = () => service.GetNlpSummaryAsync(Guid.NewGuid(), "Dolor abdominal intenso", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AiServiceTransientException>();
        ex.Which.StatusCode.Should().Be(408);
    }

    [Fact]
    public async Task PredictNoShowRiskAsync_When422Returned_ThrowsAiServicePermanentException()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent("{\"detail\":\"invalid payload\"}")
            });

        var service = CreateService(handler);
        var request = new PredictRequestDto(Guid.NewGuid(), 30, "masculino", 1, "cardiologia", 1, "ninguno");

        Func<Task> act = () => service.PredictNoShowRiskAsync(request, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AiServicePermanentException>();
        ex.Which.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task PredictNoShowRiskAsync_When200Returned_UsesRelativeUriPreservingBasePathAndParsesResponse()
    {
        Uri? capturedUri = null;
        var appointmentId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(req =>
        {
            capturedUri = req.RequestUri;
            var json = $$"""
                         {
                           "cita_id": "{{appointmentId}}",
                           "score_riesgo": 0.6800,
                           "banda_riesgo": "Moderado",
                           "clase": "no_asiste"
                         }
                         """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var service = CreateService(handler, baseAddress: "https://ai.medcare.test/v1/");
        var request = new PredictRequestDto(appointmentId, 42, "femenino", 5, "neurologia", 2, "ninguno");

        var result = await service.PredictNoShowRiskAsync(request, CancellationToken.None);

        capturedUri.Should().NotBeNull();
        capturedUri!.ToString().Should().Be("https://ai.medcare.test/v1/predict");
        result.RiskScore.Should().Be(0.6800m);
        result.RiskBand.Should().Be("Moderado");
        result.Class.Should().Be("no_asiste");
    }

    private static FastApiClinicalService CreateService(
        StubHttpMessageHandler handler,
        int timeoutSeconds = 30,
        string baseAddress = "https://ai.medcare.test/")
    {
        var tokenService = new Mock<IAiTokenService>();
        tokenService.Setup(t => t.GenerateToken()).Returns("fake-jwt-token");

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
        var options = Options.Create(new AiServiceOptions
        {
            BaseUrl = baseAddress,
            TimeoutSeconds = timeoutSeconds
        });

        return new FastApiClinicalService(
            httpClient,
            tokenService.Object,
            options,
            NullLogger<FastApiClinicalService>.Instance);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _fn;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> syncFn)
            => _fn = (req, _) => Task.FromResult(syncFn(req));

        public StubHttpMessageHandler(Func<CancellationToken, Task<HttpResponseMessage>> asyncFn)
            => _fn = (_, ct) => asyncFn(ct);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _fn(request, cancellationToken);
    }
}
