using FluentAssertions;
using MassTransit;
using MedCareOS.Application.Resolutions.Commands.ApproveResolution;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Moq;
using Omro.Contracts.Events;

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
            It.Is<ResolutionApproved>(e => e.ResolutionTaskId == task.Id && e.ResourceId == task.ResourceId),
            ct), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(ct), Times.Once);
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
