using FluentAssertions;
using MedCareOS.Domain.Services;

namespace MedCareOS.Application.UnitTests.Domain;

public class OverbookPriorityTests
{
    public static IEnumerable<object[]> TruthTableCases()
    {
        var t0 = new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);
        var origId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var overId = Guid.Parse("20000000-0000-0000-0000-000000000002");

        // 1. Original confirmó antes del deadline; sobrecupo aceptó después -> gana Original
        yield return
        [
            "Original confirmó antes que sobrecupo",
            new OverbookParticipant(origId, t0.AddHours(-5), t0.AddMinutes(10)),
            new OverbookParticipant(overId, t0.AddHours(-1), t0.AddMinutes(1)),
            OverbookWinner.Original,
            origId,
            overId,
            OverbookPriority.RuleEarlierConfirmationInstant
        ];

        // 2. Original confirmó tarde DESPUÉS de que sobrecupo aceptara -> gana Overbook
        yield return
        [
            "Original confirmó tarde con sobrecupo ya aceptado",
            new OverbookParticipant(origId, t0.AddMinutes(-5), t0.AddMinutes(1)),
            new OverbookParticipant(overId, t0.AddHours(-3), t0.AddMinutes(15)),
            OverbookWinner.Overbook,
            overId,
            origId,
            OverbookPriority.RuleEarlierConfirmationInstant
        ];

        // 3. Original nunca confirmó (null = +infinito) y llegó primero; sobrecupo sí aceptó -> gana Overbook (timestamp > orden llegada)
        yield return
        [
            "Original nunca confirmó pero llegó primero vs sobrecupo aceptado",
            new OverbookParticipant(origId, null, t0.AddMinutes(-15)),
            new OverbookParticipant(overId, t0.AddHours(-2), t0.AddMinutes(5)),
            OverbookWinner.Overbook,
            overId,
            origId,
            OverbookPriority.RuleConfirmedVsUnconfirmed
        ];

        // 4. Original confirmó, sobrecupo sin confirmación (caso defensivo) -> gana Original
        yield return
        [
            "Original confirmó vs sobrecupo sin confirmación",
            new OverbookParticipant(origId, t0.AddHours(-1), null),
            new OverbookParticipant(overId, null, t0.AddMinutes(-20)),
            OverbookWinner.Original,
            origId,
            overId,
            OverbookPriority.RuleConfirmedVsUnconfirmed
        ];

        // 5. Nadie confirmó, original hizo check-in antes -> gana Original
        yield return
        [
            "Nadie confirmó, desempate por check-in del original",
            new OverbookParticipant(origId, null, t0),
            new OverbookParticipant(overId, null, t0.AddMinutes(5)),
            OverbookWinner.Original,
            origId,
            overId,
            OverbookPriority.RuleTieBreakEarlierCheckIn
        ];

        // 6. Nadie confirmó, sobrecupo hizo check-in antes -> gana Overbook
        yield return
        [
            "Nadie confirmó, desempate por check-in del sobrecupo",
            new OverbookParticipant(origId, null, t0.AddMinutes(8)),
            new OverbookParticipant(overId, null, t0.AddMinutes(2)),
            OverbookWinner.Overbook,
            overId,
            origId,
            OverbookPriority.RuleTieBreakEarlierCheckIn
        ];

        // 7. Empate total: nadie confirmó y nadie tiene check-in -> ReceptionDecides
        yield return
        [
            "Empate total sin confirmación ni check-in",
            new OverbookParticipant(origId, null, null),
            new OverbookParticipant(overId, null, null),
            OverbookWinner.ReceptionDecides,
            null!,
            null!,
            OverbookPriority.RuleTotalTieReceptionDecides
        ];

        // 8. Empate total: nadie confirmó y mismo timestamp de check-in -> ReceptionDecides
        yield return
        [
            "Empate total sin confirmación y check-in idéntico",
            new OverbookParticipant(origId, null, t0),
            new OverbookParticipant(overId, null, t0),
            OverbookWinner.ReceptionDecides,
            null!,
            null!,
            OverbookPriority.RuleTotalTieReceptionDecides
        ];

        // 9. Empate exacto de ConfirmationInstant -> ReceptionDecides
        yield return
        [
            "Empate exacto de instante de confirmación",
            new OverbookParticipant(origId, t0.AddHours(-1), t0),
            new OverbookParticipant(overId, t0.AddHours(-1), t0.AddMinutes(5)),
            OverbookWinner.ReceptionDecides,
            null!,
            null!,
            OverbookPriority.RuleTotalTieReceptionDecides
        ];
    }

    [Theory]
    [MemberData(nameof(TruthTableCases))]
    public void Resolve_TruthTable_ShouldReturnDeterministicWinner(
        string scenario,
        OverbookParticipant original,
        OverbookParticipant overbook,
        OverbookWinner expectedWinner,
        Guid? expectedWinnerId,
        Guid? expectedDisplacedId,
        string expectedRule)
    {
        var decision = OverbookPriority.Resolve(original, overbook);

        decision.Winner.Should().Be(expectedWinner, scenario);
        decision.WinnerAppointmentId.Should().Be(expectedWinnerId, scenario);
        decision.DisplacedAppointmentId.Should().Be(expectedDisplacedId, scenario);
        decision.RuleApplied.Should().Be(expectedRule, scenario);
        decision.ReceptionInstruction.Should().NotBeNullOrWhiteSpace(scenario);
    }
}
