using System.Text.Json.Serialization;
using MassTransit;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Repositories;
using MedCareOS.Infrastructure.Configuration;
using MedCareOS.Infrastructure.Persistence;
using MedCareOS.Infrastructure.Persistence.Repositories;
using MedCareOS.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// mediatr
builder.Services.AddMediatR(cfg => 
    cfg.RegisterServicesFromAssembly(typeof(MedCareOS.Application.AssemblyReference).Assembly));

// configuracion de MassTransit con RabbitMQ
builder.Services.AddMassTransit(x =>
{
    // cloud amqp: masstransit 
    x.UsingRabbitMq((context, cfg) =>
    {
        var rmqHost = builder.Configuration["RabbitMq:Host"];
        var rmqUser = builder.Configuration["RabbitMq:Username"];
        var rmqPass = builder.Configuration["RabbitMq:Password"];

        cfg.Host(rmqHost, rmqUser, h => {
            h.Username(rmqUser);
            h.Password(rmqPass);
        });
        cfg.ConfigureEndpoints(context);
    });
});

// configuracion de JWT
var supabaseProjectId = builder.Configuration["Supabase:ProjectId"] 
    ?? throw new ArgumentNullException("Supabase ProjectId is missing in User Secrets");

var authority = $"https://{supabaseProjectId}.supabase.co/auth/v1";

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
        ValidateIssuer = false, // Supabase no valida el issuer estricto por defecto
        ValidAudiences = new[] { "authenticated" },
        ValidateAudience = true,
        ValidateLifetime = true
    };
});

builder.Services.AddAuthorization();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                       ?? throw new InvalidOperationException("La cadena de conexión 'DefaultConnection' no existe.");

builder.Services.AddDbContext<MedCareOS.Infrastructure.Persistence.ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// repositorios & unit of work
builder.Services.AddScoped<IPatientRepository, PatientRepository>();
builder.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IStaffRepository, StaffRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IBoxRepository, BoxRepository>();
builder.Services.AddScoped<IScheduleBlockRepository, ScheduleBlockRepository>();
builder.Services.AddScoped<IApplicationDbContext>(sp => 
    sp.GetRequiredService<ApplicationDbContext>());

builder.Services.Configure<AiServiceOptions>(
    builder.Configuration.GetSection(AiServiceOptions.SectionName));

builder.Services.AddSingleton<IAiTokenService, AiTokenService>();

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
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();