using System.Security.Claims;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.AspNetCore.Authentication;

namespace MedCareOS.Api.Security;

public class UserRoleClaimsTransformation : IClaimsTransformation
{
    private readonly IUserRepository _userRepository;

    private static readonly HashSet<string> DomainRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(UserRole.Paciente),
        nameof(UserRole.Recepcionista),
        nameof(UserRole.Medico),
        nameof(UserRole.Administrador),
        "omro_service"
    };

    public UserRoleClaimsTransformation(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not { IsAuthenticated: true })
            return principal;

        var existingRoles = principal.FindAll(ClaimTypes.Role)
            .Concat(principal.FindAll("role"))
            .Select(c => c.Value);

        if (existingRoles.Any(r => DomainRoles.Contains(r)))
            return principal;

        var subClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        if (!Guid.TryParse(subClaim, out var userId))
            return principal;

        var user = await _userRepository.GetByIdAsync(userId);
        if (user is not { IsActive: true })
            return principal;

        var clonedPrincipal = principal.Clone();
        if (clonedPrincipal.Identity is ClaimsIdentity identity)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToString()));
        }

        return clonedPrincipal;
    }
}
