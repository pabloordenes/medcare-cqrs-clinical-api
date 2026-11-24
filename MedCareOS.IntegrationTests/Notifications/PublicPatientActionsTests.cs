using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MassTransit.Testing;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Notifications.Events;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedCareOS.IntegrationTests.Notifications;

public class PublicPatientActionsTests : IClassFixture<CustomApiFactory>
{
    private readonly CustomApiFactory _factory;

    public PublicPatientActionsTests(CustomApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ActionPreviewAndExecute_ShouldConfirmAppointmentOnce_PublishDomainEvent_AndRejectReplayWith410()
    {
        var (appointmentId, patientId) = await SeedAppointmentAsync();
        string rawToken;

        using (var scope = _factory.Services.CreateScope())
        {
            var tokenService = scope.ServiceProvider.GetRequiredService<IPatientActionTokenService>();
            var issued = await tokenService.IssueAsync(
                appointmentId,
                patientId,
                notificationId: null,
                PatientActionScope.ConfirmOrCancel,
                TimeSpan.FromHours(24));
            rawToken = issued.RawToken;
        }

        using var anonClient = _factory.CreateClient();

        // 1. Preview twice (read-only, never consumes token, returns no clinical symptoms)
        var preview1 = await anonClient.GetAsync($"/api/public/appointments/action-preview?token={Uri.EscapeDataString(rawToken)}");
        var preview2 = await anonClient.GetAsync($"/api/public/appointments/action-preview?token={Uri.EscapeDataString(rawToken)}");

        preview1.StatusCode.Should().Be(HttpStatusCode.OK);
        preview2.StatusCode.Should().Be(HttpStatusCode.OK);

        var previewBody = await preview1.Content.ReadAsStringAsync();
        previewBody.Should().NotContain("Dolor abdominal confidencial");
        using var doc = JsonDocument.Parse(previewBody);
        doc.RootElement.GetProperty("clinicName").GetString().Should().Be("Clínica MedCare");

        // 2. First execute -> 200 OK, confirms appointment and consumes token
        var execute1 = await anonClient.PostAsJsonAsync(
            "/api/public/appointments/action",
            new { Token = rawToken, Action = "Confirm" });
        execute1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Second execute -> 410 Gone
        var execute2 = await anonClient.PostAsJsonAsync(
            "/api/public/appointments/action",
            new { Token = rawToken, Action = "Cancel" });
        execute2.StatusCode.Should().Be(HttpStatusCode.Gone);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var appointment = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == appointmentId);
            appointment.Status.Should().Be(AppointmentStatus.Confirmed);
        }

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        (await harness.Published.Any<PatientAppointmentReplyReceived>(
            p => p.Context.Message.AppointmentId == appointmentId &&
                 p.Context.Message.Intent == PatientReplyIntent.Confirm))
            .Should().BeTrue();
    }

    [Fact]
    public async Task PublicActionsEndpoint_WhenRateLimitExceeded_ShouldReturn429()
    {
        using var limitedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Notifications:RateLimiting:PublicActionsPermitLimit", "2");
            builder.UseSetting("Notifications:RateLimiting:PublicActionsWindowSeconds", "60");
        });

        using var client = limitedFactory.CreateClient();

        var res1 = await client.GetAsync("/api/public/appointments/action-preview?token=invalid");
        var res2 = await client.GetAsync("/api/public/appointments/action-preview?token=invalid");
        var res3 = await client.GetAsync("/api/public/appointments/action-preview?token=invalid");

        res1.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        res2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        res3.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private async Task<(Guid AppointmentId, Guid PatientId)> SeedAppointmentAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
        var patUser = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
        var staff = Staff.Create(staffUser.Id, "Dra", "Lagos", $"{Random.Shared.Next(10000000, 99999999)}-3", "Dermatologia", "Medico", "+56911111111");
        var patient = Patient.Create(patUser.Id, $"{Random.Shared.Next(10000000, 99999999)}-4", "Pablo", "Nunez", new DateOnly(1991, 6, 15), "M", "+56922222222", email: "pablo@test.cl");
        var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "1");
        var start = DateTime.UtcNow.AddDays(3);
        var block = ScheduleBlock.Create(staff.Id, box.Id, start, start.AddMinutes(30), Guid.NewGuid(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(start), new TimeOnly(11, 0), new TimeOnly(11, 30), [start.DayOfWeek]);
        var appointment = Appointment.Create(block.Id, patient.Id, "Dolor abdominal confidencial", "paciente");

        db.Users.AddRange(staffUser, patUser);
        db.Staffs.Add(staff);
        db.Patients.Add(patient);
        db.Boxes.Add(box);
        db.ScheduleBlocks.Add(block);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return (appointment.Id, patient.Id);
    }
}
