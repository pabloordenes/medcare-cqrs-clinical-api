namespace MedCareOS.Domain.Services;

public enum OverbookWinner
{
    Original = 0,
    Overbook = 1,
    ReceptionDecides = 2
}

public sealed record OverbookParticipant(
    Guid AppointmentId,
    DateTimeOffset? ConfirmationInstant,
    DateTimeOffset? CheckedInAt);

public sealed record OverbookPriorityDecision(
    OverbookWinner Winner,
    Guid? WinnerAppointmentId,
    Guid? DisplacedAppointmentId,
    string RuleApplied,
    string ReceptionInstruction);

public static class OverbookPriority
{
    public const string RuleConfirmedVsUnconfirmed = "CONFIRMED_VS_UNCONFIRMED";
    public const string RuleEarlierConfirmationInstant = "EARLIER_CONFIRMATION_INSTANT";
    public const string RuleTieBreakEarlierCheckIn = "TIEBREAK_EARLIER_CHECKIN";
    public const string RuleTotalTieReceptionDecides = "TOTAL_TIE_RECEPTION_DECIDES";

    /// <summary>
    /// Regla determinista de prioridad entre cita original y cita de sobrecupo:
    /// 1) Cada atención posee un ConfirmationInstant (ConfirmedAt para original, OfferAcceptedAt para sobrecupo; sin confirmación = +infinito).
    /// 2) Gana el ConfirmationInstant más antiguo (menor timestamp), ignorando orden de llegada.
    /// 3) Si ambos son +infinito, desempata CheckedInAt más antiguo.
    /// 4) Si persiste el empate (ambos null, CheckedInAt iguales, o ConfirmationInstant iguales), decide recepción.
    /// </summary>
    public static OverbookPriorityDecision Resolve(OverbookParticipant original, OverbookParticipant overbook)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(overbook);

        if (original.AppointmentId == Guid.Empty)
            throw new ArgumentException("Original.AppointmentId no puede estar vacío.", nameof(original));

        if (overbook.AppointmentId == Guid.Empty)
            throw new ArgumentException("Overbook.AppointmentId no puede estar vacío.", nameof(overbook));

        if (original.AppointmentId == overbook.AppointmentId)
            throw new ArgumentException("Original y Overbook no pueden tener el mismo AppointmentId.");

        var origConfirmed = original.ConfirmationInstant;
        var overConfirmed = overbook.ConfirmationInstant;

        if (origConfirmed.HasValue && overConfirmed.HasValue)
        {
            if (origConfirmed.Value < overConfirmed.Value)
            {
                return new OverbookPriorityDecision(
                    OverbookWinner.Original,
                    original.AppointmentId,
                    overbook.AppointmentId,
                    RuleEarlierConfirmationInstant,
                    $"Prioridad para cita original ({original.AppointmentId}): confirmó primero ({origConfirmed.Value:O} < {overConfirmed.Value:O}). Ofrecer reagendamiento al paciente de sobrecupo ({overbook.AppointmentId}).");
            }

            if (overConfirmed.Value < origConfirmed.Value)
            {
                return new OverbookPriorityDecision(
                    OverbookWinner.Overbook,
                    overbook.AppointmentId,
                    original.AppointmentId,
                    RuleEarlierConfirmationInstant,
                    $"Prioridad para paciente de sobrecupo ({overbook.AppointmentId}): aceptó antes ({overConfirmed.Value:O} < {origConfirmed.Value:O}). Mantener atención de sobrecupo y entregar alternativa/autoservicio al paciente original ({original.AppointmentId}).");
            }

            return new OverbookPriorityDecision(
                OverbookWinner.ReceptionDecides,
                null,
                null,
                RuleTotalTieReceptionDecides,
                $"Empate exacto de instante de confirmación ({origConfirmed.Value:O}) entre original ({original.AppointmentId}) y sobrecupo ({overbook.AppointmentId}). Recepción debe decidir y registrar motivo.");
        }

        if (origConfirmed.HasValue)
        {
            return new OverbookPriorityDecision(
                OverbookWinner.Original,
                original.AppointmentId,
                overbook.AppointmentId,
                RuleConfirmedVsUnconfirmed,
                $"Prioridad para cita original ({original.AppointmentId}): confirmó ({origConfirmed.Value:O}) mientras sobrecupo no registra confirmación. Desplazar sobrecupo ({overbook.AppointmentId}).");
        }

        if (overConfirmed.HasValue)
        {
            return new OverbookPriorityDecision(
                OverbookWinner.Overbook,
                overbook.AppointmentId,
                original.AppointmentId,
                RuleConfirmedVsUnconfirmed,
                $"Prioridad para paciente de sobrecupo ({overbook.AppointmentId}): aceptó el cupo ({overConfirmed.Value:O}) mientras la cita original ({original.AppointmentId}) nunca confirmó. Mantener sobrecupo y derivar al paciente original a autoservicio/reagendamiento.");
        }

        // Ambos ConfirmationInstant son +infinito -> desempate por CheckedInAt
        var origCheckIn = original.CheckedInAt;
        var overCheckIn = overbook.CheckedInAt;

        if (origCheckIn.HasValue && !overCheckIn.HasValue)
        {
            return new OverbookPriorityDecision(
                OverbookWinner.Original,
                original.AppointmentId,
                overbook.AppointmentId,
                RuleTieBreakEarlierCheckIn,
                $"Ninguno confirmó; desempate por check-in previo: atiende original ({original.AppointmentId}). Derivar sobrecupo ({overbook.AppointmentId}).");
        }

        if (overCheckIn.HasValue && !origCheckIn.HasValue)
        {
            return new OverbookPriorityDecision(
                OverbookWinner.Overbook,
                overbook.AppointmentId,
                original.AppointmentId,
                RuleTieBreakEarlierCheckIn,
                $"Ninguno confirmó; desempate por check-in previo: atiende sobrecupo ({overbook.AppointmentId}). Derivar original ({original.AppointmentId}).");
        }

        if (origCheckIn.HasValue && overCheckIn.HasValue)
        {
            if (origCheckIn.Value < overCheckIn.Value)
            {
                return new OverbookPriorityDecision(
                    OverbookWinner.Original,
                    original.AppointmentId,
                    overbook.AppointmentId,
                    RuleTieBreakEarlierCheckIn,
                    $"Ninguno confirmó; desempate por check-in más antiguo ({origCheckIn.Value:O} < {overCheckIn.Value:O}): atiende original ({original.AppointmentId}). Derivar sobrecupo ({overbook.AppointmentId}).");
            }

            if (overCheckIn.Value < origCheckIn.Value)
            {
                return new OverbookPriorityDecision(
                    OverbookWinner.Overbook,
                    overbook.AppointmentId,
                    original.AppointmentId,
                    RuleTieBreakEarlierCheckIn,
                    $"Ninguno confirmó; desempate por check-in más antiguo ({overCheckIn.Value:O} < {origCheckIn.Value:O}): atiende sobrecupo ({overbook.AppointmentId}). Derivar original ({original.AppointmentId}).");
            }
        }

        return new OverbookPriorityDecision(
            OverbookWinner.ReceptionDecides,
            null,
            null,
            RuleTotalTieReceptionDecides,
            $"Empate total entre cita original ({original.AppointmentId}) y sobrecupo ({overbook.AppointmentId}). Recepción debe decidir en sala y registrar motivo obligatorio.");
    }
}
