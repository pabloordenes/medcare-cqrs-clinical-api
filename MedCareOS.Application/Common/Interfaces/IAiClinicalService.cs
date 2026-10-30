namespace MedCareOS.Application.Common.Interfaces;

public interface IAiClinicalService
{
    Task<NlpSummaryResponseDto> GetNlpSummaryAsync(Guid appointmentId, string symptomsRaw, CancellationToken ctx); // nlp/resumen
    Task<PredictResponseDto> PredictNoShowRiskAsync(PredictRequestDto request, CancellationToken ctx); // predict
}

public record NlpSummaryResponseDto(
    string Summary,
    List<SymptomEntityDto> ExtractedSymptoms,
    string SuggestedUrgency
    );

public record SymptomEntityDto(
    string Type,
    string Text,
    string Severity
);

public record PredictRequestDto(
    Guid AppointmentId,
    int Age,
    string Gender,
    int DaysOfWaiting,
    string Specialty,
    int PreviousAbsences,
    string ReminderChannel
);

public record PredictResponseDto(
    Guid AppointmentId,
    decimal RiskScore,
    string RiskBand,
    string Class
);