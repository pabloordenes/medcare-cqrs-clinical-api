using MediatR;

namespace MedCareOS.Application.Resolutions.Commands.ApproveResolution;

public record ApproveResolutionCommand(Guid ResolutionTaskId) : IRequest;