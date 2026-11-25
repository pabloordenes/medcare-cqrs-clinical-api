using System.Text.Json;
using System.Text.Json.Serialization;
using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Api.Filters;

public class IdempotencyKeyFilter : IAsyncActionFilter
{
    private const string HeaderName = "Idempotency-Key";
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

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
        var scopedKey = BuildScopedKey(endpoint, key);

        var existing = await _dbContext.ProcessedWebhookKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.Key == scopedKey, context.HttpContext.RequestAborted);

        if (existing is not null)
        {
            if (existing.ResponseStatusCode.HasValue)
            {
                if (existing.ResponseStatusCode.Value == StatusCodes.Status204NoContent)
                {
                    context.Result = new NoContentResult();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(existing.ResponseBodyJson))
                {
                    context.Result = new ContentResult
                    {
                        StatusCode = existing.ResponseStatusCode.Value,
                        Content = existing.ResponseBodyJson,
                        ContentType = "application/json"
                    };
                    return;
                }
            }

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
            TryExtractSuccessResponse(result, out var statusCode, out var responseBodyJson))
        {
            _dbContext.ProcessedWebhookKeys.Add(
                ProcessedWebhookKey.Create(scopedKey, endpoint, statusCode, responseBodyJson));

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

    private static string BuildScopedKey(string endpoint, string key)
    {
        var raw = $"{endpoint}:{key}";
        return raw.Length <= 200 ? raw : raw[..200];
    }

    private static bool TryExtractSuccessResponse(
        IActionResult result,
        out int statusCode,
        out string? bodyJson)
    {
        bodyJson = null;

        switch (result)
        {
            case OkObjectResult okObj:
                statusCode = okObj.StatusCode ?? StatusCodes.Status200OK;
                bodyJson = okObj.Value is null ? null : JsonSerializer.Serialize(okObj.Value, SerializerOptions);
                return true;

            case CreatedResult created:
                statusCode = created.StatusCode ?? StatusCodes.Status201Created;
                bodyJson = created.Value is null ? null : JsonSerializer.Serialize(created.Value, SerializerOptions);
                return true;

            case CreatedAtActionResult createdAt:
                statusCode = createdAt.StatusCode ?? StatusCodes.Status201Created;
                bodyJson = createdAt.Value is null ? null : JsonSerializer.Serialize(createdAt.Value, SerializerOptions);
                return true;

            case ObjectResult obj when obj.StatusCode is null or (>= 200 and < 300):
                statusCode = obj.StatusCode ?? StatusCodes.Status200OK;
                bodyJson = obj.Value is null ? null : JsonSerializer.Serialize(obj.Value, SerializerOptions);
                return true;

            case OkResult:
                statusCode = StatusCodes.Status200OK;
                return true;

            case NoContentResult:
                statusCode = StatusCodes.Status204NoContent;
                return true;

            case StatusCodeResult status when status.StatusCode is >= 200 and < 300:
                statusCode = status.StatusCode;
                return true;

            default:
                statusCode = 0;
                return false;
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
