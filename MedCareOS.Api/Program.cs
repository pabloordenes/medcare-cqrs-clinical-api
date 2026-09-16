using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ═════════════════════════════════════════════════════════════════
// 1. CQRS (MediatR)
// ═════════════════════════════════════════════════════════════════
builder.Services.AddMediatR(cfg => 
    cfg.RegisterServicesFromAssembly(typeof(MedCareOS.Application.AssemblyReference).Assembly));

// ═════════════════════════════════════════════════════════════════
// 2. TICKET B0-1: MassTransit + RabbitMQ
// ═════════════════════════════════════════════════════════════════
builder.Services.AddMassTransit(x =>
{
    // Cambiamos In-Memory por RabbitMQ apuntando al Docker local
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost", "/", h => {
            h.Username("guest");
            h.Password("guest");
        });
        cfg.ConfigureEndpoints(context);
    });
});

// ═════════════════════════════════════════════════════════════════
// 3. TICKET B0-3: Autenticación con Supabase (JWT)
// ═════════════════════════════════════════════════════════════════
// Lee el secreto que guardamos en tu terminal con 'dotnet user-secrets'
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

// ═════════════════════════════════════════════════════════════════
// PIPELINE HTTP
// ═════════════════════════════════════════════════════════════════
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

app.Run();