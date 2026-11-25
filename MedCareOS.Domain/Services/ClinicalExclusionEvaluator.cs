using System.Globalization;
using System.Text;
using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Services;

public sealed record ClinicalExclusionResult(
    bool IsExcluded,
    string Code,
    string? Reason);

public static class ClinicalExclusionEvaluator
{
    public const string NoneCode = "NONE";
    public const string SeniorAgeCode = "SENIOR_AGE";
    public const string ExcludedSpecialtyCode = "EXCLUDED_SPECIALTY";
    public const string ClinicalKeywordCode = "CLINICAL_KEYWORD";

    public static ClinicalExclusionResult Evaluate(
        Patient patient,
        Staff staff,
        Appointment appointment,
        TenantConfirmationPolicy policy,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(patient);
        ArgumentNullException.ThrowIfNull(staff);
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(policy);

        var age = ComputeAge(patient.DateOfBirth, utcNow.UtcDateTime);
        if (age >= policy.ExcludedSeniorAgeYears)
        {
            return new ClinicalExclusionResult(
                IsExcluded: true,
                Code: SeniorAgeCode,
                Reason: $"Paciente adulto mayor ({age} años >= umbral {policy.ExcludedSeniorAgeYears}).");
        }

        var normalizedSpecialty = NormalizeText(staff.Specialty);
        if (!string.IsNullOrEmpty(normalizedSpecialty))
        {
            foreach (var excludedSpecialty in policy.GetExcludedClinicalSpecialties())
            {
                var normalizedExcluded = NormalizeText(excludedSpecialty);
                if (!string.IsNullOrEmpty(normalizedExcluded) &&
                    normalizedSpecialty.Contains(normalizedExcluded, StringComparison.Ordinal))
                {
                    return new ClinicalExclusionResult(
                        IsExcluded: true,
                        Code: ExcludedSpecialtyCode,
                        Reason: $"Especialidad '{staff.Specialty}' sujeta a exclusión clínica.");
                }
            }
        }

        var normalizedSymptoms = NormalizeText($"{appointment.SymptomsRaw} {appointment.ProcessedSymptoms}");
        if (!string.IsNullOrEmpty(normalizedSymptoms))
        {
            foreach (var keyword in policy.GetExcludedClinicalKeywords())
            {
                var normalizedKeyword = NormalizeText(keyword);
                if (!string.IsNullOrEmpty(normalizedKeyword) &&
                    normalizedSymptoms.Contains(normalizedKeyword, StringComparison.Ordinal))
                {
                    return new ClinicalExclusionResult(
                        IsExcluded: true,
                        Code: ClinicalKeywordCode,
                        Reason: $"Contexto clínico sensible detectado ('{keyword}').");
                }
            }
        }

        return new ClinicalExclusionResult(
            IsExcluded: false,
            Code: NoneCode,
            Reason: null);
    }

    private static int ComputeAge(DateOnly dateOfBirth, DateTime referenceUtc)
    {
        var today = DateOnly.FromDateTime(referenceUtc);
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth > today.AddYears(-age))
            age--;

        return Math.Max(0, age);
    }

    private static string NormalizeText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var decomposed = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
