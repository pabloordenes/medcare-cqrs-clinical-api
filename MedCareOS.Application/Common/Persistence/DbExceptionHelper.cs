using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Common.Persistence;

public static class DbExceptionHelper
{
    private const string PostgresUniqueViolationSqlState = "23505";

    public static bool IsUniqueConstraintViolation(DbUpdateException exception, string? constraintOrIndexName = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var inner = exception.InnerException;
        if (inner is null)
            return false;

        var sqlState = inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string;
        if (string.Equals(sqlState, PostgresUniqueViolationSqlState, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(constraintOrIndexName))
                return true;

            var constraintName = inner.GetType().GetProperty("ConstraintName")?.GetValue(inner) as string;
            if (!string.IsNullOrWhiteSpace(constraintName) &&
                constraintName.Contains(constraintOrIndexName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return !string.IsNullOrWhiteSpace(constraintOrIndexName) &&
               inner.Message.Contains(constraintOrIndexName, StringComparison.OrdinalIgnoreCase);
    }
}
