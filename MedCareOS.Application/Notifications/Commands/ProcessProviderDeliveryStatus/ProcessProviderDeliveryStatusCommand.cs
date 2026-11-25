using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.Notifications.Commands.ProcessProviderDeliveryStatus;

public record ProcessProviderDeliveryStatusCommand(
    string Provider,
    string EventId,
    string ProviderMessageId,
    NotificationStatus Status,
    DateTimeOffset EventTimestampUtc,
    string? Error = null,
    bool TriggerPatientOptOut = false) : IRequest;
