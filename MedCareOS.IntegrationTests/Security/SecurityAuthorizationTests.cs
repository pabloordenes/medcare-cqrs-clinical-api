using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedCareOS.IntegrationTests.Security;

public class SecurityAuthorizationTests : IClassFixture<CustomApiFactory>
{
    private readonly CustomApiFactory _factory;

    public SecurityAuthorizationTests(CustomApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProtectedEndpoints_WhenUnauthenticated_ShouldReturn401()
    {
        using var client = _factory.CreateClient();

        var overbookRes = await client.PostAsJsonAsync("/api/appointments/overbook",
            new { ResourceId = Guid.NewGuid(), TargetRisk = 0.90m, AutoApprove = true });
        var monitorRes = await client.PostAsJsonAsync("/api/appointments/monitor",
            new { ResourceId = Guid.NewGuid(), RequiresAction = true });
        var proposeRes = await client.PostAsJsonAsync("/api/resolutions/propose",
            new { ResourceId = Guid.NewGuid(), RiskProbability = 0.70m, AiPlanJson = "{}" });
        var createAppRes = await client.PostAsJsonAsync("/api/appointments",
            new { ScheduleBlockId = Guid.NewGuid(), PatientId = Guid.NewGuid(), SymptomsRaw = "Fiebre", SchedulingSource = "paciente" });
        var agendaRes = await client.GetAsync("/api/agenda/disponibilidad?especialidad=Cardiologia&desde=2026-11-16&hasta=2026-11-20");

        overbookRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        monitorRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        proposeRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        createAppRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        agendaRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WebhookEndpoints_WhenCalledWithUserTestToken_ShouldBeRejected_AndAcceptValidOmroRs256Jwt()
    {
        using var userClient = _factory.CreateClient();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        userClient.DefaultRequestHeaders.Add("X-Test-Role", "Administrador");

        var userAttempt = await userClient.PostAsJsonAsync("/api/resolutions/propose",
            new { ResourceId = Guid.NewGuid(), RiskProbability = 0.70m, AiPlanJson = "{}" });
        userAttempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var serviceClient = _factory.CreateClient();
        serviceClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.GenerateOmroServiceToken());

        var serviceAttempt = await serviceClient.PostAsJsonAsync("/api/resolutions/propose",
            new { ResourceId = Guid.NewGuid(), RiskProbability = 0.70m, AiPlanJson = "{\"DeferredActions\":[]}" });
        serviceAttempt.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateAppointment_WhenCalledByPacienteWithSpoofedPatientId_ShouldIgnoreBodyAndUseSubPatient()
    {
        var callerUserId = Guid.NewGuid();
        Guid callerPatientId;
        Guid victimPatientId;
        Guid blockId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var callerUser = User.Create(callerUserId, $"caller_{callerUserId:N}@test.cl", UserRole.Paciente);
            var victimUser = User.Create(Guid.NewGuid(), $"victim_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
            var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);

            var callerPatient = Patient.Create(callerUserId, $"{Random.Shared.Next(10000000, 99999999)}-1", "Juan", "Real", new DateOnly(1992, 5, 10), "M", "+56911111111");
            var victimPatient = Patient.Create(victimUser.Id, $"{Random.Shared.Next(10000000, 99999999)}-2", "Pedro", "Victima", new DateOnly(1988, 3, 15), "M", "+56922222222");
            var staff = Staff.Create(staffUser.Id, "Dra", "Casa", $"{Random.Shared.Next(10000000, 99999999)}-3", "Medicina General", "Medico", "+56933333333");
            var box = Box.Create("Box Sec 1", "Consulta", 1, "1");
            var block = ScheduleBlock.Create(
                staff.Id, box.Id,
                DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1),
                Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                new TimeOnly(10, 0), new TimeOnly(11, 0), [DateTime.UtcNow.AddDays(2).DayOfWeek]);

            db.Users.AddRange(callerUser, victimUser, staffUser);
            db.Patients.AddRange(callerPatient, victimPatient);
            db.Staffs.Add(staff);
            db.Boxes.Add(box);
            db.ScheduleBlocks.Add(block);
            await db.SaveChangesAsync();

            callerPatientId = callerPatient.Id;
            victimPatientId = victimPatient.Id;
            blockId = block.Id;
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Paciente");
        client.DefaultRequestHeaders.Add("X-Test-UserId", callerUserId.ToString());

        // Intentamos suplantar el PatientId de la víctima en el body (IDOR)
        var response = await client.PostAsJsonAsync("/api/appointments", new
        {
            ScheduleBlockId = blockId,
            PatientId = victimPatientId,
            SymptomsRaw = "Control general",
            SchedulingSource = "paciente"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var createdId = await response.Content.ReadFromJsonAsync<Guid>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var appointment = await db.Appointments.FirstAsync(a => a.Id == createdId);
            appointment.PatientId.Should().Be(callerPatientId);
            appointment.PatientId.Should().NotBe(victimPatientId);
        }
    }
}
