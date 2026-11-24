using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using MassTransit;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using JwtRegisteredClaimNames = Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames;

namespace MedCareOS.IntegrationTests;

public class CustomApiFactory : WebApplicationFactory<Program>
{
    private readonly RSA _omroTestRsa = RSA.Create(2048);
    private readonly string _dbName = $"MedCareTestDb_{Guid.NewGuid()}";

    public string OmroPublicKeyPem => _omroTestRsa.ExportSubjectPublicKeyInfoPem();
    public string TestActionTokenSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string TestMetaAppSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public string TestMetaVerifyToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    public string TestResendWebhookSecret { get; } = "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public string GenerateOmroServiceToken(string role = "omro_service")
    {
        var securityKey = new RsaSecurityKey(_omroTestRsa);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

        var claims = new[]
        {
            new Claim("role", role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = "omro-orchestrator",
            Audience = "medcareos-api",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        handler.OutboundClaimTypeMap.Clear();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Supabase:ProjectId", "test-supabase-project");
        builder.UseSetting("OmroAuth:Issuer", "omro-orchestrator");
        builder.UseSetting("OmroAuth:Audience", "medcareos-api");
        builder.UseSetting("OmroAuth:Role", "omro_service");
        builder.UseSetting("OmroAuth:PublicKeyContent", OmroPublicKeyPem);
        builder.UseSetting("AiService:BaseUrl", "http://localhost:8000");
        builder.UseSetting("AiService:Issuer", "medcareos");
        builder.UseSetting("AiService:Audience", "authenticated");
        builder.UseSetting("AiService:Role", "service-ia");
        builder.UseSetting("AiService:PrivateKeyContent", _omroTestRsa.ExportRSAPrivateKeyPem());
        builder.UseSetting("RabbitMq:RedeliveryMode", "None");
        builder.UseSetting("Scoring:PublishAnomalyOnCreate", "false");
        builder.UseSetting("Notifications:ActionTokenSecret", TestActionTokenSecret);
        builder.UseSetting("Notifications:EmailProvider", "Fake");
        builder.UseSetting("Notifications:WhatsAppProvider", "Fake");
        builder.UseSetting("Notifications:WhatsApp:AppSecret", TestMetaAppSecret);
        builder.UseSetting("Notifications:WhatsApp:WebhookVerifyToken", TestMetaVerifyToken);
        builder.UseSetting("Notifications:Resend:WebhookSecret", TestResendWebhookSecret);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IAiClinicalService));
            services.AddScoped<IAiClinicalService, StubAiClinicalService>();

            // quitar dbcontext original
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));

            // inyectar dbcontext en memoria para pruebas
            var inMemoryServiceProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName)
                    .UseInternalServiceProvider(inMemoryServiceProvider);
            });

            // reemplazar bus RabbitMQ por TestHarness en memoria
            var massTransitDescriptors = services
                .Where(d => d.ServiceType.Namespace?.StartsWith("MassTransit") == true ||
                            d.ImplementationType?.Namespace?.StartsWith("MassTransit") == true)
                .ToList();
            foreach (var descriptor in massTransitDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddMassTransitTestHarness();

            services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
                options.DefaultScheme = "Test";
            });

            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _omroTestRsa.Dispose();
        }
    }

    private sealed class StubAiClinicalService : IAiClinicalService
    {
        public Task<NlpSummaryResponseDto> GetNlpSummaryAsync(Guid appointmentId, string symptomsRaw, CancellationToken ctx)
            => Task.FromResult(new NlpSummaryResponseDto(symptomsRaw, new List<SymptomEntityDto>(), "baja"));

        public Task<PredictResponseDto> PredictNoShowRiskAsync(PredictRequestDto request, CancellationToken ctx)
            => Task.FromResult(new PredictResponseDto(request.AppointmentId, 0.10m, "Bajo", "asiste"));
    }
}
