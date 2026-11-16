using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Api.Filters;

public class IdempotencyKeyFilter : IAsyncActionFilter
{
    private const string HeaderName = "Idempotency-Key";
    private readonly IApplicationDbContext _dbContext;

    public IdempotencyKeyFilter(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var headerValues))
        {
            await next();
            return;
        }

        var key = headerValues.ToString().Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            await next();
            return;
        }

        var endpoint = context.HttpContext.Request.Path.Value ?? string.Empty;
        var scopedKey = $"{endpoint}:{key}";

        var alreadyProcessed = await _dbContext.ProcessedWebhookKeys
            .AsNoTracking()
            .AnyAsync(k => k.Key == scopedKey, context.HttpContext.RequestAborted);

        if (alreadyProcessed)
        {
            context.Result = new OkObjectResult(new
            {
                message = "Solicitud webhook ya procesada anteriormente (idempotente).",
                idempotentReplay = true
            });
            return;
        }

        var executedContext = await next();

        if (executedContext.Exception == null &&
            executedContext.Result is IActionResult result &&
            IsSuccessResult(result))
        {
            _dbContext.ProcessedWebhookKeys.Add(ProcessedWebhookKey.Create(scopedKey, endpoint));
            try
            {
                await _dbContext.SaveChangesAsync(context.HttpContext.RequestAborted);
            }
            catch (DbUpdateException)
            {
                // Carrera concurrente por misma Idempotency-Key: la primera ya fue persistida
            }
        }
    }

    private static bool IsSuccessResult(IActionResult result) => result switch
    {
        OkResult or OkObjectResult or NoContentResult or CreatedResult or CreatedAtActionResult => true,
        ObjectResult obj => obj.StatusCode is null or (>= 200 and < 300),
        StatusCodeResult status => status.StatusCode is >= 200 and < 300,
        _ => false
    };
}
