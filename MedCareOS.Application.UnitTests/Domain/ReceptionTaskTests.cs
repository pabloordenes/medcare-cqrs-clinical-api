using FluentAssertions;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.UnitTests.Domain;

public class ReceptionTaskTests
{
    [Fact]
    public void Create_WhenValid_InitializesOpenWithTrimmedReason()
    {
        var appointmentId = Guid.NewGuid();
        var patientId = Guid.NewGuid();

        var task = ReceptionTask.Create(
            appointmentId,
            patientId,
            ReceptionTaskType.ManualConfirmation,
            ReceptionTaskPriority.High,
            "  Riesgo moderado detectado por IA  ");

        task.Id.Should().NotBe(Guid.Empty);
        task.AppointmentId.Should().Be(appointmentId);
        task.PatientId.Should().Be(patientId);
        task.TaskType.Should().Be(ReceptionTaskType.ManualConfirmation);
        task.Priority.Should().Be(ReceptionTaskPriority.High);
        task.Status.Should().Be(ReceptionTaskStatus.Open);
        task.Reason.Should().Be("Riesgo moderado detectado por IA");
        task.AssignedToUserId.Should().BeNull();
        task.ResolvedAt.Should().BeNull();
    }

    [Fact]
    public void Create_WhenInvalidInputs_ThrowsArgumentException()
    {
        ((Action)(() => ReceptionTask.Create(Guid.Empty, Guid.NewGuid(), ReceptionTaskType.ManualConfirmation, ReceptionTaskPriority.High, "Motivo")))
            .Should().Throw<ArgumentException>();

        ((Action)(() => ReceptionTask.Create(Guid.NewGuid(), Guid.Empty, ReceptionTaskType.ManualConfirmation, ReceptionTaskPriority.High, "Motivo")))
            .Should().Throw<ArgumentException>();

        ((Action)(() => ReceptionTask.Create(Guid.NewGuid(), Guid.NewGuid(), ReceptionTaskType.ManualConfirmation, ReceptionTaskPriority.High, "   ")))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Assign_WhenOpen_TransitionsToInProgressAndSetsAssignee()
    {
        var task = ReceptionTask.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ReceptionTaskType.ManualConfirmation,
            ReceptionTaskPriority.Normal,
            "Contactar al paciente");
        var receptionistId = Guid.NewGuid();

        task.Assign(receptionistId);

        task.Status.Should().Be(ReceptionTaskStatus.InProgress);
        task.AssignedToUserId.Should().Be(receptionistId);
    }

    [Fact]
    public void Complete_WhenOpenOrInProgress_SetsDoneAndAuditFields()
    {
        var task = ReceptionTask.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ReceptionTaskType.ManualConfirmation,
            ReceptionTaskPriority.High,
            "Contactar al paciente");
        var receptionistId = Guid.NewGuid();
        task.Assign(receptionistId);

        task.Complete(receptionistId, "Paciente confirmó asistencia por teléfono.");

        task.Status.Should().Be(ReceptionTaskStatus.Done);
        task.ResolvedByUserId.Should().Be(receptionistId);
        task.ResolutionNotes.Should().Be("Paciente confirmó asistencia por teléfono.");
        task.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public void TransitionsAfterClosed_ThrowInvalidOperationException()
    {
        var doneTask = ReceptionTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), ReceptionTaskType.ManualConfirmation, ReceptionTaskPriority.High, "Motivo");
        doneTask.Complete(Guid.NewGuid(), "Listo");

        var cancelledTask = ReceptionTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), ReceptionTaskType.ManualConfirmation, ReceptionTaskPriority.High, "Motivo");
        cancelledTask.Cancel(Guid.NewGuid(), "Duplicada");

        ((Action)(() => doneTask.Assign(Guid.NewGuid()))).Should().Throw<InvalidOperationException>();
        ((Action)(() => doneTask.Complete(Guid.NewGuid()))).Should().Throw<InvalidOperationException>();
        ((Action)(() => doneTask.Cancel(Guid.NewGuid()))).Should().Throw<InvalidOperationException>();

        ((Action)(() => cancelledTask.Assign(Guid.NewGuid()))).Should().Throw<InvalidOperationException>();
        ((Action)(() => cancelledTask.Complete(Guid.NewGuid()))).Should().Throw<InvalidOperationException>();
        ((Action)(() => cancelledTask.Cancel(Guid.NewGuid()))).Should().Throw<InvalidOperationException>();
    }
}
