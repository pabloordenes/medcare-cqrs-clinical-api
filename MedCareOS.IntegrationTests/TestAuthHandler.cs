using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedCareOS.IntegrationTests;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader) ||
            authHeader.ToString() != "Test")
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var role = Request.Headers.TryGetValue("X-Test-Role", out var roleHeader) && !string.IsNullOrWhiteSpace(roleHeader)
            ? roleHeader.ToString()
            : "Medico";

        var userId = Request.Headers.TryGetValue("X-Test-UserId", out var userHeader) && Guid.TryParse(userHeader, out var parsedUserId)
            ? parsedUserId.ToString()
            : Guid.NewGuid().ToString();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId),
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role)
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
