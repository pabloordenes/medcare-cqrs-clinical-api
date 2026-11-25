namespace MedCareOS.Application.Resolutions.Services;

public interface IAiResolutionSupersedingService
{
    Task<int> SupersedePendingForResourceAsync(
        Guid resourceId,
        string reason,
        CancellationToken cancellationToken = default);
}
