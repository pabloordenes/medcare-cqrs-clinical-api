using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Services;

public static class RiskEscalationPolicy
{
    public static bool ShouldEmitRiskEscalated(
        Appointment appointment,
        RiskBandLevel? previousBand,
        RiskBandLevel? newBand)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        if (appointment.Status != AppointmentStatus.Scheduled ||
            appointment.OrchestrationSuppressed ||
            appointment.IsPatientConfirmed ||
            appointment.ConfirmationState == ConfirmationState.DeclinedByPatient)
        {
            return false;
        }

        // Nunca emitir en scoring inicial T0 (previousBand == null) ni por flapping ya dentro de High
        if (!previousBand.HasValue || !newBand.HasValue)
            return false;

        return previousBand.Value != RiskBandLevel.High &&
               newBand.Value == RiskBandLevel.High;
    }

    public static bool ShouldEmitConfirmationDeadlineMissed(
        Appointment appointment,
        bool transitionedToUnresponsive)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        if (!transitionedToUnresponsive)
            return false;

        return appointment.Status == AppointmentStatus.Scheduled &&
               !appointment.OrchestrationSuppressed &&
               !appointment.IsPatientConfirmed &&
               appointment.ConfirmationState == ConfirmationState.Unresponsive;
    }
}
