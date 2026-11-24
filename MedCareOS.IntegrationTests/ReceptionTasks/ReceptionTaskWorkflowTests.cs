using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using MedCareOS.Application.ReceptionTasks.Queries.GetReceptionTasks;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedCareOS.IntegrationTests.ReceptionTasks;

public class ReceptionTaskWorkflowTests : IClassFixture<CustomApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly CustomApiFactory _factory;

    public ReceptionTaskWorkflowTests(CustomApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MonitorAppointmentWebhook_ShouldCreateSingleReceptionTaskAndZeroNotifications()
    {
        var appointmentId = await SeedAppointmentAsync("Medicina General", 0.65m);

        using var omroClient = _factory.CreateClient();
        omroClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.GenerateOmroServiceToken());

        // Contrato exacto de OMRO (sin campos nuevos obligatorios)
        var payload = new { ResourceId = appointmentId, RequiresAction = true };
        var firstResponse = await omroClient.PostAsJsonAsync("/api/appointments/monitor", payload);
        var secondResponse = await omroClient.PostAsJsonAsync("/api/appointments/monitor", payload);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tasks = await db.ReceptionTasks
            .Where(t => t.AppointmentId == appointmentId)
            .ToListAsync();
        tasks.Should().HaveCount(1);
        tasks[0].TaskType.Should().Be(ReceptionTaskType.ManualConfirmation);
        tasks[0].Priority.Should().Be(ReceptionTaskPriority.High);
        tasks[0].Status.Should().Be(ReceptionTaskStatus.Open);

        var notifications = await db.Notifications
            .Where(n => n.AppointmentId == appointmentId)
            .ToListAsync();
        notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task ReceptionTasksEndpoints_ShouldListAssignCompleteAndEnforceStateAndRoleGuards()
    {
        var appointmentId1 = await SeedAppointmentAsync("Cardiologia", 0.68m);
        var appointmentId2 = await SeedAppointmentAsync("Neurologia", 0.74m);
        var receptionistId = Guid.NewGuid();

        using (var omroClient = _factory.CreateClient())
        {
            omroClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _factory.GenerateOmroServiceToken());

            (await omroClient.PostAsJsonAsync("/api/appointments/monitor",
                new { ResourceId = appointmentId1, RequiresAction = true })).EnsureSuccessStatusCode();
            (await omroClient.PostAsJsonAsync("/api/appointments/monitor",
                new { ResourceId = appointmentId2, RequiresAction = true })).EnsureSuccessStatusCode();
        }

        // Unauthenticated -> 401
        using (var anonClient = _factory.CreateClient())
        {
            var res = await anonClient.GetAsync("/api/reception-tasks");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // Paciente -> 403
        using (var patientClient = _factory.CreateClient())
        {
            patientClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
            patientClient.DefaultRequestHeaders.Add("X-Test-Role", "Paciente");
            var res = await patientClient.GetAsync("/api/reception-tasks");
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using var receptionClient = _factory.CreateClient();
        receptionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        receptionClient.DefaultRequestHeaders.Add("X-Test-Role", "Recepcionista");
        receptionClient.DefaultRequestHeaders.Add("X-Test-UserId", receptionistId.ToString());

        var listResponse = await receptionClient.GetFromJsonAsync<PagedReceptionTasksDto>(
            "/api/reception-tasks?status=Open&page=1&pageSize=50", JsonOptions);
        listResponse.Should().NotBeNull();
        var task1 = listResponse!.Items.First(t => t.AppointmentId == appointmentId1);
        var task2 = listResponse.Items.First(t => t.AppointmentId == appointmentId2);

        // Assign task 1
        var assignRes = await receptionClient.PatchAsJsonAsync($"/api/reception-tasks/{task1.Id}/assign", new { });
        assignRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Complete task 1 (204), second complete returns 409 Conflict
        var completeRes1 = await receptionClient.PatchAsJsonAsync(
            $"/api/reception-tasks/{task1.Id}/complete",
            new { ResolutionNotes = "Paciente re-confirmado por llamada." });
        var completeRes2 = await receptionClient.PatchAsJsonAsync(
            $"/api/reception-tasks/{task1.Id}/complete",
            new { ResolutionNotes = "Intento repetido." });

        completeRes1.StatusCode.Should().Be(HttpStatusCode.NoContent);
        completeRes2.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Cancel task 2
        var cancelRes = await receptionClient.PatchAsJsonAsync(
            $"/api/reception-tasks/{task2.Id}/cancel",
            new { Reason = "Paciente reagendó en sucursal." });
        cancelRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved1 = await db.ReceptionTasks.FirstAsync(t => t.Id == task1.Id);
        var saved2 = await db.ReceptionTasks.FirstAsync(t => t.Id == task2.Id);

        saved1.Status.Should().Be(ReceptionTaskStatus.Done);
        saved1.AssignedToUserId.Should().Be(receptionistId);
        saved1.ResolvedByUserId.Should().Be(receptionistId);
        saved1.ResolutionNotes.Should().Be("Paciente re-confirmado por llamada.");

        saved2.Status.Should().Be(ReceptionTaskStatus.Cancelled);
        saved2.ResolvedByUserId.Should().Be(receptionistId);
        saved2.ResolutionNotes.Should().Be("Paciente reagendó en sucursal.");
    }

    private async Task<Guid> SeedAppointmentAsync(string specialty, decimal riskScore)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
        var patientUser = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
        var staff = Staff.Create(staffUser.Id, "Dra", "Silva", $"{Random.Shared.Next(10000000, 99999999)}-1", specialty, "Medico", "+56911111111");
        var patient = Patient.Create(patientUser.Id, $"{Random.Shared.Next(10000000, 99999999)}-2", "Carlos", "Mendez", new DateOnly(1989, 7, 10), "masculino", "+56922222222");
        var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "2");
        var start = DateTime.UtcNow.AddDays(2);
        var block = ScheduleBlock.Create(
            staff.Id, box.Id, start, start.AddMinutes(30),
            Guid.NewGuid(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(start),
            new TimeOnly(10, 0), new TimeOnly(10, 30), [start.DayOfWeek]);
        var appointment = Appointment.Create(block.Id, patient.Id, "Control", "paciente");
        appointment.UpdateRiskScore(riskScore, "Moderado");

        db.Users.AddRange(staffUser, patientUser);
        db.Staffs.Add(staff);
        db.Patients.Add(patient);
        db.Boxes.Add(box);
        db.ScheduleBlocks.Add(block);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return appointment.Id;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
