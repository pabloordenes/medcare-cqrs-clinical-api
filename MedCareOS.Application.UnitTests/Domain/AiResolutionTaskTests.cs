using FluentAssertions;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.UnitTests.Domain;

public class AiResolutionTaskTests
{
    [Fact]
    public void Create_ShouldStartInPendingStatus()
    {
        var task = AiResolutionTask.Create(Guid.NewGuid(), 0.72m, "{\"DeferredActions\":[]}");

        task.Status.Should().Be(AiResolutionStatus.Pending);
        task.ApprovedBy.Should().BeNull();
        task.ApprovedAt.Should().BeNull();
    }

    [Fact]
    public void Approve_WhenPending_SetsApprovedAndAuditFields()
    {
        var adminId = Guid.NewGuid();
        var task = AiResolutionTask.Create(Guid.NewGuid(), 0.72m, "{\"DeferredActions\":[]}");

        task.Approve(adminId);

        task.Status.Should().Be(AiResolutionStatus.Approved);
        task.ApprovedBy.Should().Be(adminId);
        task.ApprovedAt.Should().NotBeNull();
    }

    [Fact]
    public void Approve_WhenAlreadyApprovedOrRejected_ThrowsInvalidOperationException()
    {
        var approvedTask = AiResolutionTask.Create(Guid.NewGuid(), 0.72m, "{}");
        approvedTask.Approve(Guid.NewGuid());

        var rejectedTask = AiResolutionTask.Create(Guid.NewGuid(), 0.72m, "{}");
        rejectedTask.Reject(Guid.NewGuid());

        ((Action)(() => approvedTask.Approve(Guid.NewGuid()))).Should().Throw<InvalidOperationException>();
        ((Action)(() => rejectedTask.Approve(Guid.NewGuid()))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reject_WhenAlreadyApprovedOrRejected_ThrowsInvalidOperationException()
    {
        var approvedTask = AiResolutionTask.Create(Guid.NewGuid(), 0.72m, "{}");
        approvedTask.Approve(Guid.NewGuid());

        var rejectedTask = AiResolutionTask.Create(Guid.NewGuid(), 0.72m, "{}");
        rejectedTask.Reject(Guid.NewGuid());

        ((Action)(() => approvedTask.Reject(Guid.NewGuid()))).Should().Throw<InvalidOperationException>();
        ((Action)(() => rejectedTask.Reject(Guid.NewGuid()))).Should().Throw<InvalidOperationException>();
    }
}
