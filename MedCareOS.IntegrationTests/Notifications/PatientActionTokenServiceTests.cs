using FluentAssertions;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Notifications.Security;
using MedCareOS.Infrastructure.Persistence;
using MedCareOS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MedCareOS.IntegrationTests.Notifications;

public class PatientActionTokenServiceTests
{
    [Fact]
    public async Task IssuePreviewAndConsume_EnforcesHmacSignatureSingleUseAndExpiration()
    {
        var dbName = $"ActionTokens_{Guid.NewGuid()}";
        var inMemoryProvider = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .UseInternalServiceProvider(inMemoryProvider)
            .Options;

        await using var db = new ApplicationDbContext(dbOptions);
        var (appointmentId, patientId) = await SeedAppointmentAsync(db);

        var service = new PatientActionTokenService(
            new PatientActionTokenRepository(db),
            db,
            new UnitOfWork(db),
            Options.Create(new NotificationOptions
            {
                ClinicName = "Clínica MedCare",
                TimeZoneId = "America/Santiago",
                ActionTokenSecret = "unit-test-hmac-secret-at-least-32-bytes-long!"
            }));

        var issued = await service.IssueAsync(
            appointmentId,
            patientId,
            notificationId: null,
            PatientActionScope.ConfirmOrCancel,
            TimeSpan.FromHours(24));

        // Database must store SHA-256 hash, NEVER raw token
        var stored = await db.PatientActionTokens.SingleAsync(t => t.Id == issued.TokenId);
        stored.TokenHash.Should().HaveLength(64);
        stored.TokenHash.Should().NotBe(issued.RawToken);

        // Preview is strictly read-only (calling twice leaves UsedAt null)
        var preview1 = await service.PreviewAsync(issued.RawToken);
        var preview2 = await service.PreviewAsync(issued.RawToken);
        preview1.IsValid.Should().BeTrue();
        preview1.ClinicName.Should().Be("Clínica MedCare");
        preview1.AllowedActions.Should().Contain(PatientReplyIntent.Confirm);
        preview2.IsValid.Should().BeTrue();

        // Tampered token fails HMAC check
        var tampered = issued.RawToken[..^2] + "XX";
        var tamperedPreview = await service.PreviewAsync(tampered);
        tamperedPreview.IsValid.Should().BeFalse();
        tamperedPreview.ValidationStatus.Should().Be(ActionTokenValidationStatus.InvalidSignatureOrFormat);

        // Single-use consume succeeds once
        var consume1 = await service.ConsumeAsync(issued.RawToken, PatientReplyIntent.Confirm);
        consume1.IsSuccess.Should().BeTrue();
        consume1.AppointmentId.Should().Be(appointmentId);

        // Replay attempt is rejected as AlreadyUsed
        var consume2 = await service.ConsumeAsync(issued.RawToken, PatientReplyIntent.Cancel);
        consume2.IsSuccess.Should().BeFalse();
        consume2.ValidationStatus.Should().Be(ActionTokenValidationStatus.AlreadyUsed);

        // Expired token is rejected
        var expiredIssued = await service.IssueAsync(
            appointmentId,
            patientId,
            notificationId: null,
            PatientActionScope.ConfirmOrCancel,
            TimeSpan.FromMilliseconds(10));
        await Task.Delay(30);

        var expiredResult = await service.ConsumeAsync(expiredIssued.RawToken, PatientReplyIntent.Confirm);
        expiredResult.IsSuccess.Should().BeFalse();
        expiredResult.ValidationStatus.Should().Be(ActionTokenValidationStatus.Expired);
    }

    private static async Task<(Guid AppointmentId, Guid PatientId)> SeedAppointmentAsync(ApplicationDbContext db)
    {
        var staffUser = User.Create(Guid.NewGuid(), $"doc_{Guid.NewGuid():N}@test.cl", UserRole.Medico);
        var patUser = User.Create(Guid.NewGuid(), $"pat_{Guid.NewGuid():N}@test.cl", UserRole.Paciente);
        var staff = Staff.Create(staffUser.Id, "Dr", "Perez", $"{Random.Shared.Next(10000000, 99999999)}-1", "Cardiologia", "Medico", "+56911111111");
        var patient = Patient.Create(patUser.Id, $"{Random.Shared.Next(10000000, 99999999)}-2", "Elena", "Vargas", new DateOnly(1990, 8, 12), "F", "+56922222222", email: "elena@test.cl");
        var box = Box.Create($"Box_{Guid.NewGuid():N}", "Consulta", 1, "1");
        var start = DateTime.UtcNow.AddDays(2);
        var block = ScheduleBlock.Create(staff.Id, box.Id, start, start.AddMinutes(30), Guid.NewGuid(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(start), new TimeOnly(10, 0), new TimeOnly(10, 30), [start.DayOfWeek]);
        var appointment = Appointment.Create(block.Id, patient.Id, "Control", "paciente");

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
