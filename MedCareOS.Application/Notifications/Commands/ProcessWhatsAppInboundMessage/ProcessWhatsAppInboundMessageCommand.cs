using MediatR;

namespace MedCareOS.Application.Notifications.Commands.ProcessWhatsAppInboundMessage;

public record ProcessWhatsAppInboundMessageCommand(
    string InboundMessageId,
    string FromPhone,
    string? ButtonPayload,
    string? TextBody,
    DateTimeOffset ReceivedAtUtc) : IRequest;
