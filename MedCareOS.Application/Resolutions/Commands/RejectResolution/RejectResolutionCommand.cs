using MediatR;

namespace MedCareOS.Application.Resolutions.Commands.RejectResolution;

public record RejectResolutionCommand(Guid ResolutionTaskId, Guid? RejectedBy = null) : IRequest;
