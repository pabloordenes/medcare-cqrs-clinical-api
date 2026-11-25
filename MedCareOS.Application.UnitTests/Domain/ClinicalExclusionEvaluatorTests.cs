using FluentAssertions;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Services;

namespace MedCareOS.Application.UnitTests.Domain;

public class ClinicalExclusionEvaluatorTests
{
    [Fact]
    public void Evaluate_WhenPatientIsSenior_ShouldReturnSeniorAgeExclusion()
    {
        var now = new DateTimeOffset(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);
        var policy = TenantConfirmationPolicy.CreateDefault(RiskThresholds.DefaultTenantId, now);
        var patient = Patient.Create(Guid.NewGuid(), "5.555.555-5", "Rosa", "Diaz", new DateOnly(1950, 5, 10), "F", "+56911111111");
        var staff = Staff.Create(Guid.NewGuid(), "Dr", "Perez", "9.999.999-9", "Cardiologia", "Medico", "+56922222222");
        var appointment = Appointment.Create(Guid.NewGuid(), patient.Id, "Control anual", "paciente");

        var result = ClinicalExclusionEvaluator.Evaluate(patient, staff, appointment, policy, now);

        result.IsExcluded.Should().BeTrue();
        result.Code.Should().Be(ClinicalExclusionEvaluator.SeniorAgeCode);
    }

    [Fact]
    public void Evaluate_WhenSpecialtyHasAccentsMatchingExcluded_ShouldReturnExcludedSpecialty()
    {
        var now = new DateTimeOffset(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);
        var policy = TenantConfirmationPolicy.CreateDefault(RiskThresholds.DefaultTenantId, now);
        var patient = Patient.Create(Guid.NewGuid(), "15.555.555-5", "Camila", "Soto", new DateOnly(1992, 5, 10), "F", "+56911111111");
        var staff = Staff.Create(Guid.NewGuid(), "Dra", "Mendez", "9.999.999-8", "Oncología Médica", "Medico", "+56922222222");
        var appointment = Appointment.Create(Guid.NewGuid(), patient.Id, "Seguimiento", "paciente");

        var result = ClinicalExclusionEvaluator.Evaluate(patient, staff, appointment, policy, now);

        result.IsExcluded.Should().BeTrue();
        result.Code.Should().Be(ClinicalExclusionEvaluator.ExcludedSpecialtyCode);
    }

    [Fact]
    public void Evaluate_WhenSymptomsContainExcludedKeyword_ShouldReturnClinicalKeywordExclusion()
    {
        var now = new DateTimeOffset(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);
        var policy = TenantConfirmationPolicy.CreateDefault(RiskThresholds.DefaultTenantId, now);
        var patient = Patient.Create(Guid.NewGuid(), "16.555.555-5", "Diego", "Vera", new DateOnly(1990, 3, 15), "M", "+56911111111");
        var staff = Staff.Create(Guid.NewGuid(), "Dr", "Rojas", "9.999.999-7", "Traumatologia", "Medico", "+56922222222");
        var appointment = Appointment.Create(Guid.NewGuid(), patient.Id, "Evaluación pre-operatorio rodilla", "paciente");

        var result = ClinicalExclusionEvaluator.Evaluate(patient, staff, appointment, policy, now);

        result.IsExcluded.Should().BeTrue();
        result.Code.Should().Be(ClinicalExclusionEvaluator.ClinicalKeywordCode);
    }

    [Fact]
    public void Evaluate_WhenNoExclusionApplies_ShouldReturnNotExcluded()
    {
        var now = new DateTimeOffset(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);
        var policy = TenantConfirmationPolicy.CreateDefault(RiskThresholds.DefaultTenantId, now);
        var patient = Patient.Create(Guid.NewGuid(), "17.555.555-5", "Sofia", "Lagos", new DateOnly(1995, 7, 20), "F", "+56911111111");
        var staff = Staff.Create(Guid.NewGuid(), "Dra", "Bravo", "9.999.999-6", "Dermatologia", "Medico", "+56922222222");
        var appointment = Appointment.Create(Guid.NewGuid(), patient.Id, "Control de lunares", "paciente");

        var result = ClinicalExclusionEvaluator.Evaluate(patient, staff, appointment, policy, now);

        result.IsExcluded.Should().BeFalse();
        result.Code.Should().Be(ClinicalExclusionEvaluator.NoneCode);
        result.Reason.Should().BeNull();
    }
}
