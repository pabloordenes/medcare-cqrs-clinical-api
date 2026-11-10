using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedCareOS.IntegrationTests;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, 
        ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) {}

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // si no trae la cabecera exacta devolvemos NoResult() para que devuelva 401
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader) || 
            authHeader.ToString() != "Test")
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
        
        // fabricamos usuario con el rol que el sistema espera
        var claims = new[] 
        { 
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), 
            new Claim(ClaimTypes.Role, "Medico") 
        };
        
        // asignamos la entidad al esquema test
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        // decimos que el usuario paso la validacion exitosamente
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}