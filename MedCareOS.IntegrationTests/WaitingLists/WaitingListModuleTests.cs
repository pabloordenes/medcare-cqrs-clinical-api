using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using MedCareOS.Application.WaitingLists.Queries;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedCareOS.IntegrationTests.WaitingLists;

public class WaitingListModuleTests : IClassFixture<CustomApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly CustomApiFactory _factory;

    public WaitingListModuleTests(CustomApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task JoinWaitingList_ByAuthenticatedPaciente_ShouldUseSubPatient_ForceZeroPriority_AndAllowMeAndCancel()
    {
        var callerUserId = Guid.NewGuid();
        var victimUserId = Guid.NewGuid();
        Guid callerPatientId;
        Guid victimPatientId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var callerUser = User.Create(callerUserId, $"wl_caller_{callerUserId:N}@test.cl", UserRole.Paciente);
            var victimUser = User.Create(victimUserId, $"wl_victim_{victimUserId:N}@test.cl", UserRole.Paciente);
            var callerPatient = Patient.Create(callerUserId, $"{Random.Shared.Next(10000000, 99999999)}-1", "Camila", "Soto", new DateOnly(1990, 4, 12), "F", "+56910000001");
            var victimPatient = Patient.Create(victimUserId, $"{Random.Shared.Next(10000000, 99999999)}-2", "Mario", "Perez", new DateOnly(1985, 8, 20), "M", "+56910000002");

            db.Users.AddRange(callerUser, victimUser);
            db.Patients.AddRange(callerPatient, victimPatient);
            await db.SaveChangesAsync();

            callerPatientId = callerPatient.Id;
            victimPatientId = victimPatient.Id;
        }

        using var client = CreateClient(callerUserId, "Paciente");

        var joinRes = await client.PostAsJsonAsync("/api/waiting-list", new
        {
            PatientId = victimPatientId,
            Specialty = "Cardiología",
            Priority = 999,
            ClinicalContextJson = "{\"urgencia\":\"dolor toracico leve\"}"
        });

        joinRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var payload = await joinRes.Content.ReadFromJsonAsync<JsonElement>();
        var createdId = payload.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var entry = await db.WaitingLists.FirstAsync(w => w.Id == createdId);
            entry.PatientId.Should().Be(callerPatientId);
            entry.PatientId.Should().NotBe(victimPatientId);
            entry.Priority.Should().Be(0);
            entry.SpecialtyNormalized.Should().Be("cardiologia");
            entry.Status.Should().Be(WaitingListStatus.Active);
        }

        var meRes = await client.GetAsync("/api/waiting-list/me");
        meRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var myEntries = await meRes.Content.ReadFromJsonAsync<List<WaitingListEntryDto>>(JsonOptions);
        myEntries.Should().ContainSingle(e => e.Id == createdId && e.Status == WaitingListStatus.Active);

        var delRes = await client.DeleteAsync($"/api/waiting-list/{createdId}?reason=Ya+consegui+hora");
        delRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Tras cancelar, el paciente puede volver a inscribirse en la misma especialidad
        var rejoinRes = await client.PostAsJsonAsync("/api/waiting-list", new
        {
            Specialty = "cardiologia"
        });
        rejoinRes.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task JoinWaitingList_DuplicateActiveSpecialtyNormalized_ShouldReturn409Conflict()
    {
        var userId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Users.Add(User.Create(userId, $"wl_dup_{userId:N}@test.cl", UserRole.Paciente));
            db.Patients.Add(Patient.Create(userId, $"{Random.Shared.Next(10000000, 99999999)}-3", "Elena", "Diaz", new DateOnly(1993, 2, 1), "F", "+56910000003"));
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(userId, "Paciente");

        var first = await client.PostAsJsonAsync("/api/waiting-list", new { Specialty = "Dermatología" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await client.PostAsJsonAsync("/api/waiting-list", new { Specialty = "  dermatologia " });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task WaitingList_IdorAndRbac_ShouldBlockUnauthorizedAccess_AndRankCandidatesWithDisplacedBonus()
    {
        var patientAUserId = Guid.NewGuid();
        var patientBUserId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        Guid patientAId;
        Guid patientBId;
        Guid displacedAppointmentId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userA = User.Create(patientAUserId, $"wl_a_{patientAUserId:N}@test.cl", UserRole.Paciente);
            var userB = User.Create(patientBUserId, $"wl_b_{patientBUserId:N}@test.cl", UserRole.Paciente);
            var userStaff = User.Create(staffUserId, $"wl_doc_{staffUserId:N}@test.cl", UserRole.Medico);

            var patientA = Patient.Create(patientAUserId, $"{Random.Shared.Next(10000000, 99999999)}-4", "Lucas", "Mora", new DateOnly(1987, 6, 15), "M", "+56910000004");
            var patientB = Patient.Create(patientBUserId, $"{Random.Shared.Next(10000000, 99999999)}-5", "Sofia", "Vera", new DateOnly(1991, 9, 25), "F", "+56910000005");
            var staff = Staff.Create(staffUserId, "Dr", "Rojas", $"{Random.Shared.Next(10000000, 99999999)}-6", "Traumatología", "Medico", "+56910000006");
            var box = Box.Create("Box WL 1", "Consulta", 1, "1");
            var block = ScheduleBlock.Create(
                staff.Id, box.Id,
                DateTime.UtcNow.AddDays(3), DateTime.UtcNow.AddDays(3).AddHours(1),
                Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
                new TimeOnly(9, 0), new TimeOnly(10, 0), [DateTime.UtcNow.AddDays(3).DayOfWeek]);
            var appointmentA = Appointment.Create(block.Id, patientA.Id, "Control");
            appointmentA.MarkDisplacedByOverbook(DateTimeOffset.UtcNow);

            db.Users.AddRange(userA, userB, userStaff);
            db.Patients.AddRange(patientA, patientB);
            db.Staffs.Add(staff);
            db.Boxes.Add(box);
            db.ScheduleBlocks.Add(block);
            db.Appointments.Add(appointmentA);
            await db.SaveChangesAsync();

            patientAId = patientA.Id;
            patientBId = patientB.Id;
            displacedAppointmentId = appointmentA.Id;
        }

        using var clientA = CreateClient(patientAUserId, "Paciente");
        using var clientB = CreateClient(patientBUserId, "Paciente");
        using var receptionistClient = CreateClient(Guid.NewGuid(), "Recepcionista");

        // Patient B intenta apropiarse de la cita desplazada de A -> 403
        var stealBonusRes = await clientB.PostAsJsonAsync("/api/waiting-list", new
        {
            Specialty = "Traumatologia",
            DisplacedSourceAppointmentId = displacedAppointmentId
        });
        stealBonusRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Patient A usa su propia cita desplazada -> 201
        var joinARes = await clientA.PostAsJsonAsync("/api/waiting-list", new
        {
            Specialty = "Traumatología",
            DisplacedSourceAppointmentId = displacedAppointmentId
        });
        joinARes.StatusCode.Should().Be(HttpStatusCode.Created);
        var entryAId = (await joinARes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Patient B intenta eliminar inscripción de Patient A -> 403 IDOR
        var deleteIdorRes = await clientB.DeleteAsync($"/api/waiting-list/{entryAId}");
        deleteIdorRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Patient A intenta listar candidatos globales -> 403 RBAC
        var listAsPatientRes = await clientA.GetAsync("/api/waiting-list?specialty=Traumatologia");
        listAsPatientRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Recepción inscribe a B con prioridad alta (5), pero A tiene bonus DisplacedByOverbook (1000 > 500)
        var joinBRes = await receptionistClient.PostAsJsonAsync("/api/waiting-list", new
        {
            PatientId = patientBId,
            Specialty = "Traumatologia",
            Priority = 5
        });
        joinBRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var entryBId = (await joinBRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var candidatesRes = await receptionistClient.GetAsync("/api/waiting-list?specialty=Traumatologia");
        candidatesRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var candidates = await candidatesRes.Content.ReadFromJsonAsync<List<WaitingListEntryDto>>(JsonOptions);

        candidates.Should().NotBeNull();
        candidates!.Select(c => c.Id).Should().ContainInOrder(entryAId, entryBId);
        candidates[0].IsDisplacedBonus.Should().BeTrue();
        candidates[0].RankScore.Should().NotBeNull();
        candidates[1].RankScore.Should().NotBeNull();
        candidates[0].RankScore!.Value.Should().BeGreaterThan(candidates[1].RankScore!.Value);
    }

    private HttpClient CreateClient(Guid userId, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }
}
