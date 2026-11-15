using MediatR;

namespace MedCareOS.Application.Resolutions.Commands.ReceiveAiProposal;

public record ReceiveAiProposalCommand(
    Guid ResourceId,
    decimal RiskProbability,
    string AiPlanJson
    ) : IRequest<Guid>;