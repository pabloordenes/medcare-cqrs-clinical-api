using System.Security.Cryptography;
using FluentAssertions;
using MedCareOS.Infrastructure.Configuration;
using MedCareOS.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace MedCareOS.Infrastructure.UnitTests.Services;

public class AiTokenServiceTests
{
    [Fact]
    public void Constructor_WhenNoKeyIsProvided_ThrowsInvalidOperationException()
    {
        // Arrange
        // Simulamos el desastre en Render: no hay variable de entorno ni archivo local
        var options = Options.Create(new AiServiceOptions
        {
            PrivateKeyContent = string.Empty,
            PrivateKeyPath = string.Empty
        });

        // Act
        // Encapsulamos la instanciación para atrapar la explosión
        Action act = () => new AiTokenService(options);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No se encontró la clave privada RSA*");
    }

    [Fact]
    public void Constructor_WhenEnvVariableIsProvided_InitializesSuccessfully()
    {
        // Arrange
        // Generamos una llave RSA real al vuelo para engañar a la validación criptográfica
        using var tempRsa = RSA.Create();
        var validPemKey = tempRsa.ExportRSAPrivateKeyPem();

        // Simulamos que Render inyectó el contenido correctamente
        var options = Options.Create(new AiServiceOptions
        {
            PrivateKeyContent = validPemKey,
            PrivateKeyPath = string.Empty, // El archivo no importa si la variable existe
            Issuer = "MedCareOS",
            Audience = "AiWorkers",
            Role = "system"
        });

        // Act
        Action act = () => new AiTokenService(options);

        // Assert
        // Si el código híbrido funciona, no debe lanzar ninguna excepción
        act.Should().NotThrow();
    }
}