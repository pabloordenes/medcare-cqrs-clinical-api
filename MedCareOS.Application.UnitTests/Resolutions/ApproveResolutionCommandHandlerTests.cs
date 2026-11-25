using System.Text.Json;
using FluentAssertions;
using MassTransit;
using MedCareOS.Application.Resolutions.Commands.ApproveResolution;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Moq;
using Omro.Contracts.Events;
using Omro.Contracts.Plans;

namespace MedCareOS.Application.UnitTests.Resolutions;

public class ApproveResolutionCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenPending_PublishesResolutionApprovedBeforeSaveChangesForBusOutboxAtomicity()
    {
        var ct = CancellationToken.None;
        var task = AiResolutionTask.Create(Guid.NewGuid(), 0.72m, "{\"DeferredActions\":[]}");
        var approverId = Guid.NewGuid();

        var repo = new Mock<IAiResolutionTaskRepository>();
        repo.Setup(r => r.GetByIdAsync(task.Id, ct)).ReturnsAsync(task);

        var appointmentRepo = new Mock<IAppointmentRepository>();
        appointmentRepo.Setup(r => r.GetAppointmentByIdAsync(task.ResourceId, ct)).ReturnsAsync((Appointment?)null);

        var sequence = new MockSequence();
        var publishEndpoint = new Mock<IPublishEndpoint>();
        var unitOfWork = new Mock<IUnitOfWork>();

        publishEndpoint
            .InSequence(sequence)
            .Setup(p => p.Publish(It.IsAny<ResolutionApproved>(), ct))
            .Returns(Task.CompletedTask);

        unitOfWork
            .InSequence(sequence)
            .Setup(u => u.SaveChangesAsync(ct))
            .ReturnsAsync(1);

        var handler = new ApproveResolutionCommandHandler(
            repo.Object,
            appointmentRepo.Object,
            unitOfWork.Object,
            publishEndpoint.Object);

        await handler.Handle(new ApproveResolutionCommand(task.Id, approverId), ct);

        task.Status.Should().Be(AiResolutionStatus.Approved);
        task.ApprovedBy.Should().Be(approverId);
        publishEndpoint.Verify(p => p.Publish(
            It.Is<ResolutionApproved>(e =>
                e.ResolutionTaskId == task.Id &&
                e.ResourceId == task.ResourceId &&
                e.SchemaVersion == PlanContractVersions.V1Legacy &&
                e.AiPlan == null),
            ct), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(ct), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTaskContainsStructuredV2Plan_ShouldPublishResolutionApprovedV2WithTypedAiPlan()
    {
        var ct = CancellationToken.None;
        var resourceId = Guid.NewGuid();
        var omroTaskId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(3);
        var structuredJson = JsonSerializer.Serialize(new StructuredAiPlan(
            SchemaVersion: PlanContractVersions.V2Structured,
            Title: "HITL Overbook",
            Reasoning: "Requiere aprobación clínica",
            ConfidenceScore: 0.87m,
            OmroTaskId: omroTaskId,
            MatchedRuleId: "deadline-moderate-or-excluded-hitl",
            PolicyVersion: 1,
            ProposalExpiresAt: expiresAt,
            IsShadowModeFallback: false,
            DeferredActions:
            [
                new DeferredActionContract(
                    "GENERATE_OVERBOOK",
                    "https://medcare.test/api/appointments/overbook",
                    "POST",
                    JsonSerializer.SerializeToElement(new { resourceId = resourceId.ToString(), decisionMode = "Approved" }))
            ]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var task = AiResolutionTask.Create(resourceId, 0.78m, structuredJson);
        var approverId = Guid.NewGuid();

        var repo = new Mock<IAiResolutionTaskRepository>();
        repo.Setup(r => r.GetByIdAsync(task.Id, ct)).ReturnsAsync(task);

        var appointmentRepo = new Mock<IAppointmentRepository>();
        appointmentRepo.Setup(r => r.GetAppointmentByIdAsync(resourceId, ct)).ReturnsAsync((Appointment?)null);

        ResolutionApproved? publishedEvent = null;
        var publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .Setup(p => p.Publish(It.IsAny<ResolutionApproved>(), ct))
            .Callback<object, CancellationToken>((ev, _) => publishedEvent = (ResolutionApproved)ev)
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(ct)).ReturnsAsync(1);

        var handler = new ApproveResolutionCommandHandler(
            repo.Object,
            appointmentRepo.Object,
            unitOfWork.Object,
            publishEndpoint.Object);

        await handler.Handle(new ApproveResolutionCommand(task.Id, approverId), ct);

        publishedEvent.Should().NotBeNull();
        publishedEvent!.SchemaVersion.Should().Be(PlanContractVersions.V2Structured);
        publishedEvent.AiPlan.Should().NotBeNull();
        publishedEvent.AiPlan!.OmroTaskId.Should().Be(omroTaskId);
        publishedEvent.AiPlan.DeferredActions.Should().ContainSingle(a =>
            a.ActionType == "GENERATE_OVERBOOK" &&
            a.HttpMethod == "POST");
    }

    [Fact]
    public async Task Handle_WhenAppointmentAlreadyConfirmedOrSuppressed_SupersedesTaskAndThrowsWithoutPublishing()
    {
        var ct = CancellationToken.None;
        var appointment = Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Control", "paciente");
        appointment.RegisterPatientConfirmation(DateTimeOffset.UtcNow);

        var task = AiResolutionTask.Create(appointment.Id, 0.78m, "{\"DeferredActions\":[]}");

        var repo = new Mock<IAiResolutionTaskRepository>();
        repo.Setup(r => r.GetByIdAsync(task.Id, ct)).ReturnsAsync(task);

        var appointmentRepo = new Mock<IAppointmentRepository>();
        appointmentRepo.Setup(r => r.GetAppointmentByIdAsync(appointment.Id, ct)).ReturnsAsync(appointment);

        var publishEndpoint = new Mock<IPublishEndpoint>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new ApproveResolutionCommandHandler(
            repo.Object,
            appointmentRepo.Object,
            unitOfWork.Object,
            publishEndpoint.Object);

        Func<Task> act = () => handler.Handle(new ApproveResolutionCommand(task.Id, Guid.NewGuid()), ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        task.Status.Should().Be(AiResolutionStatus.Superseded);
        publishEndpoint.Verify(p => p.Publish(It.IsAny<ResolutionApproved>(), ct), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(ct), Times.Once);
    }
}
