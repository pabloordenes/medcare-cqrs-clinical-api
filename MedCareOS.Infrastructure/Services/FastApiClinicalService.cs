using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedCareOS.Infrastructure.Services;

public class FastApiClinicalService : IAiClinicalService
{
    private readonly HttpClient _httpClient;
    private readonly IAiTokenService _tokenService;
    private readonly AiServiceOptions _options;
    private readonly ILogger<FastApiClinicalService> _logger;

    public FastApiClinicalService(
        HttpClient httpClient,
        IAiTokenService tokenService,
        IOptions<AiServiceOptions> options,
        ILogger<FastApiClinicalService> logger)
    {
        _httpClient = httpClient;
        _tokenService = tokenService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<NlpSummaryResponseDto> GetNlpSummaryAsync(
        Guid appointmentId,
        string symptomsRaw,
        CancellationToken cancellationToken = default)
    {
        var timeoutSeconds = _options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 30;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var token = _tokenService.GenerateToken();
            var payload = new NlpResumenApiRequest(appointmentId, symptomsRaw);

            using var request = new HttpRequestMessage(HttpMethod.Post, "nlp/resumen")
            {
                Content = JsonContent.Create(payload),
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
            };

            using var response = await _httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowClassifiedHttpExceptionAsync("nlp/resumen", appointmentId, response, cts.Token);
            }

            var apiResult = await response.Content.ReadFromJsonAsync<NlpResumenApiResponse>(cancellationToken: cts.Token);
            if (apiResult == null || string.IsNullOrWhiteSpace(apiResult.Resumen))
            {
                throw new AiServicePermanentException(
                    $"La respuesta del servicio NLP para la cita {appointmentId} fue nula o vacía.");
            }

            var symptoms = apiResult.EntidadesExtraidas?
                .Select(e => new SymptomEntityDto(e.Tipo, e.Texto, e.Severidad ?? "medio"))
                .ToList() ?? new List<SymptomEntityDto>();

            return new NlpSummaryResponseDto(
                Summary: apiResult.Resumen,
                ExtractedSymptoms: symptoms,
                SuggestedUrgency: apiResult.UrgenciaSugerida ?? "baja");
        }
        catch (AiServiceTransientException)
        {
            throw;
        }
        catch (AiServicePermanentException)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Timeout ({TimeoutSeconds}s) en microservicio NLP para cita {AppointmentId}.", timeoutSeconds, appointmentId);
            throw new AiServiceTransientException($"Timeout ({timeoutSeconds}s) al invocar nlp/resumen para la cita {appointmentId}.", 408, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON inválido recibido desde microservicio NLP para cita {AppointmentId}.", appointmentId);
            throw new AiServicePermanentException($"Respuesta JSON inválida de nlp/resumen para la cita {appointmentId}.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            var statusCode = ex.StatusCode.HasValue ? (int?)ex.StatusCode.Value : null;
            if (IsPermanentStatusCode(statusCode))
            {
                _logger.LogError(ex, "Error HTTP permanente {StatusCode} en microservicio NLP para cita {AppointmentId}.", statusCode, appointmentId);
                throw new AiServicePermanentException($"Error permanente HTTP {statusCode} en nlp/resumen para cita {appointmentId}.", statusCode, ex);
            }

            _logger.LogWarning(ex, "Error HTTP transitorio {StatusCode} en microservicio NLP para cita {AppointmentId}.", statusCode, appointmentId);
            throw new AiServiceTransientException($"Error transitorio en nlp/resumen para cita {appointmentId}: {ex.Message}", statusCode, ex);
        }
    }

    public async Task<PredictResponseDto> PredictNoShowRiskAsync(
        PredictRequestDto requestDto,
        CancellationToken cancellationToken = default)
    {
        var timeoutSeconds = _options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 30;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var token = _tokenService.GenerateToken();

            var mappedGender = requestDto.Gender?.Trim().ToLowerInvariant() switch
            {
                "masculino" => "M",
                "femenino" => "F",
                _ => "Otro"
            };

            var payload = new PredictApiRequest(
                CitaId: requestDto.AppointmentId,
                Edad: requestDto.Age,
                Genero: mappedGender,
                DiasEspera: requestDto.DaysOfWaiting,
                Especialidad: requestDto.Specialty,
                AusenciasPrevias: requestDto.PreviousAbsences,
                CanalRecordatorio: requestDto.ReminderChannel);

            using var request = new HttpRequestMessage(HttpMethod.Post, "predict")
            {
                Content = JsonContent.Create(payload),
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
            };

            using var response = await _httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowClassifiedHttpExceptionAsync("predict", requestDto.AppointmentId, response, cts.Token);
            }

            var apiResult = await response.Content.ReadFromJsonAsync<PredictApiResponse>(cancellationToken: cts.Token);
            if (apiResult == null || string.IsNullOrWhiteSpace(apiResult.BandaRiesgo))
            {
                throw new AiServicePermanentException(
                    $"La respuesta del servicio predict para la cita {requestDto.AppointmentId} fue nula o inválida.");
            }

            return new PredictResponseDto(
                AppointmentId: apiResult.CitaId,
                RiskScore: apiResult.ScoreRiesgo,
                RiskBand: apiResult.BandaRiesgo,
                Class: apiResult.Clase);
        }
        catch (AiServiceTransientException)
        {
            throw;
        }
        catch (AiServicePermanentException)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Timeout ({TimeoutSeconds}s) en microservicio predict para cita {AppointmentId}.", timeoutSeconds, requestDto.AppointmentId);
            throw new AiServiceTransientException($"Timeout ({timeoutSeconds}s) al invocar predict para la cita {requestDto.AppointmentId}.", 408, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON inválido recibido desde microservicio predict para cita {AppointmentId}.", requestDto.AppointmentId);
            throw new AiServicePermanentException($"Respuesta JSON inválida de predict para la cita {requestDto.AppointmentId}.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            var statusCode = ex.StatusCode.HasValue ? (int?)ex.StatusCode.Value : null;
            if (IsPermanentStatusCode(statusCode))
            {
                _logger.LogError(ex, "Error HTTP permanente {StatusCode} en microservicio predict para cita {AppointmentId}.", statusCode, requestDto.AppointmentId);
                throw new AiServicePermanentException($"Error permanente HTTP {statusCode} en predict para cita {requestDto.AppointmentId}.", statusCode, ex);
            }

            _logger.LogWarning(ex, "Error HTTP transitorio {StatusCode} en microservicio predict para cita {AppointmentId}.", statusCode, requestDto.AppointmentId);
            throw new AiServiceTransientException($"Error transitorio en predict para cita {requestDto.AppointmentId}: {ex.Message}", statusCode, ex);
        }
    }

    private async Task ThrowClassifiedHttpExceptionAsync(
        string endpoint,
        Guid appointmentId,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;
        string body = string.Empty;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 300) body = body[..300];
        }
        catch
        {
            // Ignoramos errores al leer cuerpo de diagnóstico
        }

        if (IsPermanentStatusCode(statusCode))
        {
            _logger.LogError(
                "Error HTTP permanente {StatusCode} al invocar {Endpoint} para cita {AppointmentId}. Body={Body}",
                statusCode,
                endpoint,
                appointmentId,
                body);
            throw new AiServicePermanentException(
                $"Error permanente HTTP {statusCode} en {endpoint} para cita {appointmentId}.",
                statusCode);
        }

        _logger.LogWarning(
            "Error HTTP transitorio {StatusCode} al invocar {Endpoint} para cita {AppointmentId}. Body={Body}",
            statusCode,
            endpoint,
            appointmentId,
            body);
        throw new AiServiceTransientException(
            $"Error transitorio HTTP {statusCode} en {endpoint} para cita {appointmentId}.",
            statusCode);
    }

    private static bool IsPermanentStatusCode(int? statusCode)
    {
        return statusCode is >= 400 and < 500 and not 408 and not 429;
    }

    private record NlpResumenApiRequest(
        [property: JsonPropertyName("cita_id")] Guid CitaId,
        [property: JsonPropertyName("texto_sintomas")] string TextoSintomas
    );

    private record EntidadSintomaApiResponse(
        [property: JsonPropertyName("tipo")] string Tipo,
        [property: JsonPropertyName("texto")] string Texto,
        [property: JsonPropertyName("severidad")] string? Severidad
    );

    private record NlpResumenApiResponse(
        [property: JsonPropertyName("resumen")] string Resumen,
        [property: JsonPropertyName("entidades_extraidas")] List<EntidadSintomaApiResponse>? EntidadesExtraidas,
        [property: JsonPropertyName("urgencia_sugerida")] string? UrgenciaSugerida
    );

    private record PredictApiRequest(
        [property: JsonPropertyName("cita_id")] Guid CitaId,
        [property: JsonPropertyName("edad")] int Edad,
        [property: JsonPropertyName("genero")] string Genero,
        [property: JsonPropertyName("dias_espera")] int DiasEspera,
        [property: JsonPropertyName("especialidad")] string Especialidad,
        [property: JsonPropertyName("ausencias_previas")] int AusenciasPrevias,
        [property: JsonPropertyName("canal_recordatorio")] string CanalRecordatorio
    );

    private record PredictApiResponse(
        [property: JsonPropertyName("cita_id")] Guid CitaId,
        [property: JsonPropertyName("score_riesgo")] decimal ScoreRiesgo,
        [property: JsonPropertyName("banda_riesgo")] string BandaRiesgo,
        [property: JsonPropertyName("clase")] string Clase
    );
}
