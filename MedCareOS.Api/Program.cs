using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using MassTransit;
using MedCareOS.Api.BackgroundServices;
using MedCareOS.Api.Security;
using MedCareOS.Application.Appointments.Consumers;
using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Application.Appointments.Overbooking;
using MedCareOS.Application.Appointments.Risk;
using MedCareOS.Application.Appointments.Workers;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Common.Persistence;
using MedCareOS.Application.Interfaces;
using MedCareOS.Application.Notifications.Consumers;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Notifications.Templates;
using MedCareOS.Application.Resolutions.Services;
using MedCareOS.Application.WaitingLists.Services;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Infrastructure.Configuration;
using MedCareOS.Infrastructure.Messaging.Admin;
using MedCareOS.Infrastructure.Messaging.Configuration;
using MedCareOS.Infrastructure.Messaging.Consumers;
using MedCareOS.Infrastructure.Messaging.Observers;
using MedCareOS.Infrastructure.Notifications.Channels;
using MedCareOS.Infrastructure.Notifications.Security;
using MedCareOS.Infrastructure.Notifications.Webhooks;
using MedCareOS.Infrastructure.Persistence;
using MedCareOS.Infrastructure.Persistence.Repositories;
using MedCareOS.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// mediatr
builder.Services.AddMediatR(cfg => 
    cfg.RegisterServicesFromAssembly(typeof(MedCareOS.Application.AssemblyReference).Assembly));

builder.Services.Configure<ScoringOptions>(
    builder.Configuration.GetSection(ScoringOptions.SectionName));
builder.Services.Configure<ScoringRetryOptions>(
    builder.Configuration.GetSection(ScoringRetryOptions.SectionName));
builder.Services.Configure<ScheduledActionsOptions>(
    builder.Configuration.GetSection(ScheduledActionsOptions.SectionName));
builder.Services.Configure<RabbitMqMessagingOptions>(
    builder.Configuration.GetSection(RabbitMqMessagingOptions.SectionName));
builder.Services.Configure<NotificationOptions>(
    builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.Configure<ResendOptions>(
    builder.Configuration.GetSection(ResendOptions.SectionName));
builder.Services.Configure<MetaWhatsAppOptions>(
    builder.Configuration.GetSection(MetaWhatsAppOptions.SectionName));

var notificationOptions = builder.Configuration
    .GetSection(NotificationOptions.SectionName)
    .Get<NotificationOptions>() ?? new NotificationOptions();

var rabbitOptions = builder.Configuration
    .GetSection(RabbitMqMessagingOptions.SectionName)
    .Get<RabbitMqMessagingOptions>() ?? new RabbitMqMessagingOptions();

builder.Services.AddSingleton<IOperationalAlertSink, LoggingOperationalAlertSink>();
builder.Services.AddSingleton<DeadLetterAlertObserver>();
builder.Services.AddScoped<IMessageInboxStore, EfMessageInboxStore>();
builder.Services.AddScoped<IRabbitMqQueueAdminService, RabbitMqQueueAdminService>();

if (string.Equals(rabbitOptions.RedeliveryMode, "QuartzInMemory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddQuartz();
    builder.Services.AddQuartzHostedService(options =>
    {
        options.WaitForJobsToComplete = true;
    });
}

// configuracion de MassTransit con RabbitMQ + EF Core Bus Outbox
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();
    x.AddConsumer<ScoreAppointmentConsumer, ScoreAppointmentConsumerDefinition>();
    x.AddConsumer<SendNotificationConsumer, SendNotificationConsumerDefinition>();
    x.AddConsumer<LateConfirmationReceivedConsumer, LateConfirmationReceivedConsumerDefinition>();
    x.AddConsumer<MedCareOS.Application.WaitingLists.Consumers.OverbookOpenedConsumer, OverbookOpenedConsumerDefinition>();
    x.AddConsumer<MedCareOS.Application.WaitingLists.Consumers.SlotReleasedConsumer, SlotReleasedConsumerDefinition>();

    x.AddEntityFrameworkOutbox<ApplicationDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
        o.QueryDelay = TimeSpan.FromSeconds(5);
        o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
    });

    if (string.Equals(rabbitOptions.RedeliveryMode, "QuartzInMemory", StringComparison.OrdinalIgnoreCase))
    {
        x.AddPublishMessageScheduler();
        x.AddQuartzConsumers();
    }
    else if (string.Equals(rabbitOptions.RedeliveryMode, "RabbitMqDelayedExchange", StringComparison.OrdinalIgnoreCase))
    {
        x.AddDelayedMessageScheduler();
    }

    x.UsingRabbitMq((context, cfg) =>
    {
        var rmqHost = string.IsNullOrWhiteSpace(rabbitOptions.Host) ? "localhost" : rabbitOptions.Host;
        var rmqUser = string.IsNullOrWhiteSpace(rabbitOptions.Username) ? "guest" : rabbitOptions.Username;
        var rmqPass = string.IsNullOrWhiteSpace(rabbitOptions.Password) ? "guest" : rabbitOptions.Password;
        var rmqVHost = rabbitOptions.VirtualHost;
        if (string.IsNullOrWhiteSpace(rmqVHost))
        {
            rmqVHost = rmqHost is "localhost" or "127.0.0.1" or "rabbitmq" ? "/" : rmqUser;
        }

        cfg.Host(rmqHost, rmqVHost, h =>
        {
            h.Username(rmqUser);
            h.Password(rmqPass);
        });

        if (string.Equals(rabbitOptions.RedeliveryMode, "QuartzInMemory", StringComparison.OrdinalIgnoreCase))
        {
            cfg.UsePublishMessageScheduler();
        }
        else if (string.Equals(rabbitOptions.RedeliveryMode, "RabbitMqDelayedExchange", StringComparison.OrdinalIgnoreCase))
        {
            cfg.UseDelayedMessageScheduler();
        }

        cfg.ConnectReceiveObserver(context.GetRequiredService<DeadLetterAlertObserver>());
        cfg.ConfigureEndpoints(context);
    });
});

// configuracion de JWT
var supabaseProjectId = builder.Configuration["Supabase:ProjectId"] 
    ?? throw new ArgumentNullException("Supabase ProjectId is missing in User Secrets");

var authority = $"https://{supabaseProjectId}.supabase.co/auth/v1";

builder.Services.Configure<OmroAuthOptions>(
    builder.Configuration.GetSection(OmroAuthOptions.SectionName));

var omroAuthOptions = builder.Configuration
    .GetSection(OmroAuthOptions.SectionName)
    .Get<OmroAuthOptions>() ?? new OmroAuthOptions();

RsaSecurityKey? omroPublicKey = null;
string? omroPem = !string.IsNullOrWhiteSpace(omroAuthOptions.PublicKeyContent)
    ? omroAuthOptions.PublicKeyContent
    : (!string.IsNullOrWhiteSpace(omroAuthOptions.PublicKeyPath) && File.Exists(omroAuthOptions.PublicKeyPath)
        ? File.ReadAllText(omroAuthOptions.PublicKeyPath)
        : null);

if (string.IsNullOrWhiteSpace(omroPem))
{
    var aiPrivateKeyContent = builder.Configuration["AiService:PrivateKeyContent"];
    var aiPrivateKeyPath = builder.Configuration["AiService:PrivateKeyPath"];

    if (!string.IsNullOrWhiteSpace(aiPrivateKeyContent))
    {
        omroPem = aiPrivateKeyContent;
    }
    else if (!string.IsNullOrWhiteSpace(aiPrivateKeyPath) && File.Exists(aiPrivateKeyPath))
    {
        omroPem = File.ReadAllText(aiPrivateKeyPath);
    }
    else if (File.Exists("/etc/secrets/dotnet-private.pem"))
    {
        omroPem = File.ReadAllText("/etc/secrets/dotnet-private.pem");
    }
}

if (!string.IsNullOrWhiteSpace(omroPem))
{
    var rsa = RSA.Create();
    rsa.ImportFromPem(omroPem.Replace("\\n", "\n").ToCharArray());
    omroPublicKey = new RsaSecurityKey(rsa);
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.Authority = authority;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        ValidateIssuer = true,
        ValidIssuer = authority,
        ValidAudiences = new[] { "authenticated" },
        ValidateAudience = true,
        ValidateLifetime = true,
        RoleClaimType = ClaimTypes.Role
    };
})
.AddJwtBearer("OmroService", options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = omroPublicKey,
        ValidateIssuer = true,
        ValidIssuer = string.IsNullOrWhiteSpace(omroAuthOptions.Issuer) ? "omro-orchestrator" : omroAuthOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = string.IsNullOrWhiteSpace(omroAuthOptions.Audience) ? "medcareos-api" : omroAuthOptions.Audience,
        ValidateLifetime = true,
        RoleClaimType = "role"
    };
});

builder.Services.AddScoped<IClaimsTransformation, UserRoleClaimsTransformation>();

var omroRole = string.IsNullOrWhiteSpace(omroAuthOptions.Role) ? "omro_service" : omroAuthOptions.Role;

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OmroService", policy =>
    {
        policy.AddAuthenticationSchemes("OmroService");
        policy.RequireClaim("role", omroRole);
    });

    options.AddPolicy("Administrador", policy =>
        policy.RequireRole(nameof(UserRole.Administrador)));

    options.AddPolicy("Recepcionista", policy =>
        policy.RequireRole(nameof(UserRole.Recepcionista), nameof(UserRole.Administrador)));

    options.AddPolicy("Medico", policy =>
        policy.RequireRole(nameof(UserRole.Medico), nameof(UserRole.Administrador)));

    options.AddPolicy("Paciente", policy =>
        policy.RequireRole(nameof(UserRole.Paciente), nameof(UserRole.Recepcionista), nameof(UserRole.Administrador)));
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                       ?? throw new InvalidOperationException("La cadena de conexión 'DefaultConnection' no existe.");

builder.Services.AddDbContext<MedCareOS.Infrastructure.Persistence.ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// repositorios & unit of work
builder.Services.AddScoped<IPatientRepository, PatientRepository>();
builder.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();
builder.Services.AddScoped<IAppointmentScheduledActionRepository, AppointmentScheduledActionRepository>();
builder.Services.AddScoped<IRiskSignalRepository, RiskSignalRepository>();
builder.Services.AddScoped<ITenantConfirmationPolicyRepository, TenantConfirmationPolicyRepository>();
builder.Services.AddScoped<IAppointmentLifecyclePlanner, AppointmentLifecyclePlanner>();
builder.Services.AddScoped<IRiskSignalProcessor, RiskSignalProcessor>();
builder.Services.AddScoped<IOmroAnomalyPublisher, OmroAnomalyPublisher>();
builder.Services.AddScoped<IAppointmentRescoreService, AppointmentRescoreService>();
builder.Services.AddSingleton<IScheduledActionsProcessor, ScheduledActionsProcessor>();
builder.Services.AddScoped<ITransactionalExecutor, EfTransactionalExecutor>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IStaffRepository, StaffRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IBoxRepository, BoxRepository>();
builder.Services.AddScoped<IScheduleBlockRepository, ScheduleBlockRepository>();
builder.Services.AddScoped<IWaitingListRepository, WaitingListRepository>();
builder.Services.AddScoped<IWaitingListOfferRepository, WaitingListOfferRepository>();
builder.Services.AddScoped<MedCareOS.Application.WaitingLists.Ranking.IWaitingListRanker, MedCareOS.Application.WaitingLists.Ranking.DeterministicWaitingListRanker>();
builder.Services.AddScoped<IWaitingListBackfillService, MedCareOS.Application.WaitingLists.Services.WaitingListBackfillService>();
builder.Services.AddScoped<IOverbookCollisionEventRepository, OverbookCollisionEventRepository>();
builder.Services.AddScoped<IAppointmentRescheduleHistoryRepository, AppointmentRescheduleHistoryRepository>();
builder.Services.AddScoped<MedCareOS.Application.Appointments.Services.ISlotReleaseService, MedCareOS.Application.Appointments.Services.SlotReleaseService>();
builder.Services.AddScoped<IWaitingListOfferCancellationService, MedCareOS.Application.WaitingLists.Services.WaitingListOfferCancellationService>();
builder.Services.AddSingleton<MedCareOS.Application.WaitingLists.Workers.IWaitingListOfferExpirationProcessor, MedCareOS.Application.WaitingLists.Workers.WaitingListOfferExpirationProcessor>();
builder.Services.AddScoped<IOverbookCollisionResolutionService, OverbookCollisionResolutionService>();
builder.Services.AddScoped<IAiResolutionTaskRepository, AiResolutionTaskRepository>();
builder.Services.AddScoped<IAiResolutionSupersedingService, AiResolutionSupersedingService>();
builder.Services.AddScoped<IReceptionTaskRepository, ReceptionTaskRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<IPatientActionTokenRepository, PatientActionTokenRepository>();
builder.Services.AddScoped<IApplicationDbContext>(sp => 
    sp.GetRequiredService<ApplicationDbContext>());

builder.Services.AddSingleton<IFakeNotificationStore, InMemoryFakeNotificationStore>();
builder.Services.AddScoped<INotificationChannel, FakeEmailNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, FakeWhatsAppNotificationChannel>();
builder.Services.AddHttpClient<ResendEmailNotificationChannel>((serviceProvider, client) =>
{
    var resendOptions = serviceProvider.GetRequiredService<IOptions<ResendOptions>>().Value;
    var baseUrl = string.IsNullOrWhiteSpace(resendOptions.BaseUrl) ? "https://api.resend.com/" : resendOptions.BaseUrl;
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
});
builder.Services.AddTransient<INotificationChannel>(sp => sp.GetRequiredService<ResendEmailNotificationChannel>());
builder.Services.AddHttpClient<MetaWhatsAppNotificationChannel>((serviceProvider, client) =>
{
    var waOptions = serviceProvider.GetRequiredService<IOptions<MetaWhatsAppOptions>>().Value;
    var baseUrl = string.IsNullOrWhiteSpace(waOptions.BaseUrl) ? "https://graph.facebook.com/" : waOptions.BaseUrl;
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
});
builder.Services.AddTransient<INotificationChannel>(sp => sp.GetRequiredService<MetaWhatsAppNotificationChannel>());
builder.Services.AddScoped<INotificationChannelResolver, NotificationChannelResolver>();
builder.Services.AddScoped<INotificationTemplateRenderer, NotificationTemplateRenderer>();
builder.Services.AddScoped<INotificationOutboxService, NotificationOutboxService>();
builder.Services.AddScoped<IPatientActionTokenService, PatientActionTokenService>();
builder.Services.AddScoped<IWebhookSignatureVerifier, MetaWebhookSignatureVerifier>();
builder.Services.AddScoped<IWebhookSignatureVerifier, ResendWebhookSignatureVerifier>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("ProviderWebhooks", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var limit = Math.Max(1, notificationOptions.RateLimiting.ProviderWebhooksPermitLimit);
        var windowSeconds = Math.Max(1, notificationOptions.RateLimiting.ProviderWebhooksWindowSeconds);

        return RateLimitPartition.GetFixedWindowLimiter(
            $"webhooks:{ip}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("PublicPatientActions", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var limit = Math.Max(1, notificationOptions.RateLimiting.PublicActionsPermitLimit);
        var windowSeconds = Math.Max(1, notificationOptions.RateLimiting.PublicActionsWindowSeconds);

        return RateLimitPartition.GetFixedWindowLimiter(
            $"public_actions:{ip}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

builder.Services.Configure<AiServiceOptions>(
    builder.Configuration.GetSection(AiServiceOptions.SectionName));

builder.Services.AddSingleton<IAiTokenService, AiTokenService>();
builder.Services.AddHostedService<NoShowDetectionBackgroundService>();
builder.Services.AddHostedService<ScoringRetryBackgroundService>();
builder.Services.AddHostedService<NotificationRetentionBackgroundService>();
builder.Services.AddHostedService<ScheduledActionsWorker>();
builder.Services.AddHostedService<WaitingListOfferExpirationWorker>();

builder.Services.AddHttpClient<IAiClinicalService, FastApiClinicalService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<AiServiceOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {   
        // deserializamos string a Enum
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// pipeline http
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();



// IMPORTANTE: El orden de estos middlewares es crítico
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();