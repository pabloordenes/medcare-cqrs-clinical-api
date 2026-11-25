using FluentAssertions;
using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Entities;

namespace MedCareOS.Application.UnitTests.Architecture;

public class CleanArchitectureTests
{
    [Fact]
    public void Domain_ShouldNotReferenceApplicationInfrastructureApiOrmOrMessaging()
    {
        var domainAssembly = typeof(Appointment).Assembly;
        var referencedNames = domainAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        referencedNames.Should().NotContain(n =>
            n.Equals("MedCareOS.Application", StringComparison.OrdinalIgnoreCase)
            || n.Equals("MedCareOS.Infrastructure", StringComparison.OrdinalIgnoreCase)
            || n.Equals("MedCareOS.Api", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("MassTransit", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Application_ShouldNotReferenceInfrastructureApiOrNpgsql()
    {
        var applicationAssembly = typeof(IApplicationDbContext).Assembly;
        var referencedNames = applicationAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        referencedNames.Should().NotContain(n =>
            n.Equals("MedCareOS.Infrastructure", StringComparison.OrdinalIgnoreCase)
            || n.Equals("MedCareOS.Api", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
    }
}
