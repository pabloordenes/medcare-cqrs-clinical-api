using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MedCareOS.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Infrastructure.Services;

public class FastApiClinicalService : IAiClinicalService
{
    private readonly HttpClient _httpClient;
    private readonly IAiTokenService _tokenService;
    private readonly ILogger<FastApiClinicalService> _logger;

    public FastApiClinicalService(
        HttpClient httpClient,
        IAiTokenService tokenService,
        ILogger<FastApiClinicalService> logger)
    {
        _httpClient = httpClient;
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<NlpSummaryResponseDto> GetNlpSummaryAsync(
        Guid appointmentId, 
        string symptomsRaw,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(4));

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
            response.EnsureSuccessStatusCode();

            var apiResult =
                await response.Content.ReadFromJsonAsync<NlpResumenApiResponse>(cancellationToken: cts.Token);

            if (apiResult == null)
                throw new InvalidOperationException("La respuesta del servicio NLP fue nula");

            var symptoms = apiResult.EntidadesExtraidas?
                .Select(e => new SymptomEntityDto(e.Tipo, e.Texto, e.Severidad ?? "medio"))
                .ToList() ?? new List<SymptomEntityDto>();

            return new NlpSummaryResponseDto(
                Summary: apiResult.Resumen,
                ExtractedSymptoms: symptoms,
                SuggestedUrgency: apiResult.UrgenciaSugerida ?? "baja"
                );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"El microservicio NLP en FastAPI no respondió a tiempo para la cita {appointmentId}. Aplicando fallback.", appointmentId);

            return new NlpSummaryResponseDto(
                Summary: symptomsRaw,
                ExtractedSymptoms: new List<SymptomEntityDto>(),
                SuggestedUrgency: "baja");
        }
    }

    public async Task<PredictResponseDto> PredictNoShowRiskAsync(
        PredictRequestDto requestDto, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(4));

        try
        {
            var token = _tokenService.GenerateToken();

            var payload = new PredictApiRequest(
                CitaId: requestDto.AppointmentId,
                Edad: requestDto.Age,
                Genero: requestDto.Gender,
                DiasEspera: requestDto.DaysOfWaiting,
                Especialidad: requestDto.Specialty,
                AusenciasPrevias: requestDto.PreviousAbsences,
                CanalRecordatorio: requestDto.ReminderChannel
            );

            using var request = new HttpRequestMessage(HttpMethod.Post, "/predict")
            {
                Content = JsonContent.Create(payload),
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
            };

            using var response = await _httpClient.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            var apiResult = await response.Content.ReadFromJsonAsync<PredictApiResponse>(cancellationToken: cts.Token);

            if (apiResult == null)
                throw new InvalidOperationException("La respuesta del servicio de predicción fue nula.");

            return new PredictResponseDto(
                AppointmentId: apiResult.CitaId,
                RiskScore: apiResult.ScoreRiesgo,
                RiskBand: apiResult.BandaRiesgo,
                Class: apiResult.Clase
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"El microservicio predict en FastAPI no respondió a tiempo para la cita {requestDto.AppointmentId}. Aplicando fallback.", requestDto.AppointmentId);

            return new PredictResponseDto(
                AppointmentId: requestDto.AppointmentId,
                RiskScore: 0m,
                RiskBand: "Pendiente",
                Class: "asiste"
            );
        }
    }

    // dtos privados de infrastructure 
    
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