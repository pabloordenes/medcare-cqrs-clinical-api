using MedCareOS.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Resolutions.Services;

public class AiResolutionSupersedingService : IAiResolutionSupersedingService
{
    private readonly IAiResolutionTaskRepository _taskRepository;
    private readonly ILogger<AiResolutionSupersedingService> _logger;

    public AiResolutionSupersedingService(
        IAiResolutionTaskRepository taskRepository,
        ILogger<AiResolutionSupersedingService> logger)
    {
        _taskRepository = taskRepository;
        _logger = logger;
    }

    public async Task<int> SupersedePendingForResourceAsync(
        Guid resourceId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var pending = await _taskRepository.GetAllPendingByResourceIdAsync(resourceId, cancellationToken);
        if (pending.Count == 0)
            return 0;

        foreach (var task in pending)
        {
            task.Supersede();
        }

        _logger.LogInformation(
            "{Count} tarea(s) HITL de OMRO marcadas como Superseded para recurso {ResourceId}. Motivo: {Reason}",
            pending.Count,
            resourceId,
            reason);

        return pending.Count;
    }
}
