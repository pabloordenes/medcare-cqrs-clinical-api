using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using MedCareOS.Application.Patients.Queries.GetPatientNotificationPreferences;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace MedCareOS.IntegrationTests.Patients;

public class PatientNotificationPreferencesTests : IClassFixture<CustomApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly CustomApiFactory _factory;

    public PatientNotificationPreferencesTests(CustomApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SelfServicePreferencesEndpoints_ShouldUpdateConsentAndOptOutForAuthenticatedPatientOnly()
    {
        var userId = Guid.NewGuid();
        Guid patientId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = User.Create(userId, $"pat_{userId:N}@test.cl", UserRole.Paciente);
            var patient = Patient.Create(
                userId,
                $"{Random.Shared.Next(10000000, 99999999)}-1",
                "Javiera",
                "Muñoz",
                new DateOnly(1993, 4, 15),
                "femenino",
                "+56933334444",
                email: "javiera@test.cl");

            db.Users.Add(user);
            db.Patients.Add(patient);
            await db.SaveChangesAsync();
            patientId = patient.Id;
        }

        using var patientClient = _factory.CreateClient();
        patientClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        patientClient.DefaultRequestHeaders.Add("X-Test-Role", "Paciente");
        patientClient.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());

        // Attempting WhatsApp preferred without opt-in -> 409 Conflict
        var invalidUpdate = await patientClient.PutAsJsonAsync(
            "/api/patients/me/notification-preferences",
            new { PreferredChannel = "WhatsApp", WhatsAppOptIn = false });
        invalidUpdate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Patient cannot access another patient's admin endpoint -> 403 Forbidden
        var forbiddenAdminRead = await patientClient.GetAsync($"/api/patients/{patientId}/notification-preferences");
        forbiddenAdminRead.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Valid update opting into WhatsApp
        var updateRes = await patientClient.PutAsJsonAsync(
            "/api/patients/me/notification-preferences",
            new { PreferredChannel = "WhatsApp", WhatsAppOptIn = true, Source = "patient_portal" });
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await updateRes.Content.ReadFromJsonAsync<PatientNotificationPreferencesDto>(JsonOptions);
        updated.Should().NotBeNull();
        updated!.PreferredChannel.Should().Be(NotificationChannel.WhatsApp);
        updated.EffectiveReminderChannel.Should().Be("whatsapp");
        updated.WhatsAppOptIn.Should().BeTrue();
        updated.WhatsAppOptInSource.Should().Be("patient_portal");
        updated.HasOptedOut.Should().BeFalse();

        // Opt-out via self-service
        var optOutRes = await patientClient.PostAsJsonAsync("/api/patients/me/opt-out", new { Source = "patient_portal" });
        optOutRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterOptOut = await patientClient.GetFromJsonAsync<PatientNotificationPreferencesDto>(
            "/api/patients/me/notification-preferences", JsonOptions);
        afterOptOut!.HasOptedOut.Should().BeTrue();
        afterOptOut.EffectiveReminderChannel.Should().Be("ninguno");
        afterOptOut.WhatsAppOptIn.Should().BeFalse();
    }

    [Fact]
    public async Task ReceptionistPreferencesEndpoints_ShouldManagePatientConsent()
    {
        Guid patientId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var patient = Patient.Create(
                user.Id,
                $"{Random.Shared.Next(10000000, 99999999)}-2",
                "Matias",
                "Rojas",
                new DateOnly(1987, 9, 5),
                "masculino",
                "+56955556666",
                email: "matias@test.cl");

            db.Users.Add(user);
            db.Patients.Add(patient);
            await db.SaveChangesAsync();
            patientId = patient.Id;
        }

        using var receptionClient = _factory.CreateClient();
        receptionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        receptionClient.DefaultRequestHeaders.Add("X-Test-Role", "Recepcionista");

        var putRes = await receptionClient.PutAsJsonAsync(
            $"/api/patients/{patientId}/notification-preferences",
            new { PreferredChannel = "WhatsApp", WhatsAppOptIn = true });
        putRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await receptionClient.GetFromJsonAsync<PatientNotificationPreferencesDto>(
            $"/api/patients/{patientId}/notification-preferences", JsonOptions);
        dto!.WhatsAppOptIn.Should().BeTrue();
        dto.WhatsAppOptInSource.Should().Be("reception_desk");
        dto.EffectiveReminderChannel.Should().Be("whatsapp");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
