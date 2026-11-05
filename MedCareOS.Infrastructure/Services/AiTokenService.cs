using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using MedCareOS.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using JwtRegisteredClaimNames = Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames;

namespace MedCareOS.Infrastructure.Services;

public class AiTokenService : IAiTokenService
{
    private readonly AiServiceOptions _options;
    private readonly RSA _rsa;
    private readonly SigningCredentials _signingCredentials;

    public AiTokenService(IOptions<AiServiceOptions> options)
    {
        _options = options.Value;
        _rsa = RSA.Create();
        LoadPrivateKey();
        var securityKey = new RsaSecurityKey(_rsa);
        _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
    }

    private void LoadPrivateKey()
    {
        string privateKeyContent;
        
        if (!string.IsNullOrWhiteSpace(_options.PrivateKeyContent))
        {
            privateKeyContent = _options.PrivateKeyContent;
        }
        // entorno local
        else if (!string.IsNullOrWhiteSpace(_options.PrivateKeyPath) && File.Exists(_options.PrivateKeyPath))
        {
            privateKeyContent = File.ReadAllText(_options.PrivateKeyPath);
        }
        else
        {
            throw new InvalidOperationException(
                "No se encontró la clave privada RSA. " +
                "Debe proveer AIService:PrivateKeyContent o una ruta válida en AiService:PrivateKeyPath."
            );
        }
        
        _rsa.ImportFromPem(privateKeyContent.ToCharArray());
    }

    public string GenerateToken()
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Iss, _options.Issuer),
            new Claim(JwtRegisteredClaimNames.Aud, _options.Audience),
            new Claim("role", _options.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = _signingCredentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }
}
