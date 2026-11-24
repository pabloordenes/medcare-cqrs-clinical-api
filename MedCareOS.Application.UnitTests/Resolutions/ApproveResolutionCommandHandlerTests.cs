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

        var handler = new ApproveResolutionCommandHandler(repo.Object, unitOfWork.Object, publishEndpoint.Object);

        await handler.Handle(new ApproveResolutionCommand(task.Id, approverId), ct);

        task.Status.Should().Be(AiResolutionStatus.Approved);
        task.ApprovedBy.Should().Be(approverId);
        publishEndpoint.Verify(p => p.Publish(
            It.Is<ResolutionApproved>(e => e.ResolutionTaskId == task.Id && e.ResourceId == task.ResourceId),
            ct), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(ct), Times.Once);
    }
}
