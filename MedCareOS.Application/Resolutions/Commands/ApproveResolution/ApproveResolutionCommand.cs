using MediatR;

namespace MedCareOS.Application.Resolutions.Commands.ApproveResolution;

public record ApproveResolutionCommand(Guid ResolutionTaskId, Guid? ApprovedBy = null) : IRequest;
