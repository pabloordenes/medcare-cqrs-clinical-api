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
    private readonly SigningCredentials _signingCredentials;

    public AiTokenService(IOptions<AiServiceOptions> options)
    {
        _options = options.Value;
        
        if (!File.Exists(_options.PrivateKeyPath))
            throw new FileNotFoundException($"No se encontró la clave privada en {_options.PrivateKeyPath}");
        
        string pemText = File.ReadAllText(_options.PrivateKeyPath);
        
        var rsa = RSA.Create();
        rsa.ImportFromPem(pemText);
        
        var securityKey = new RsaSecurityKey(rsa);
        _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
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

        string tokenString = tokenHandler.WriteToken(token);

        return tokenString;
    }
}
