using System.Text.Json;
using FluentAssertions;
using MassTransit;
using MedCareOS.Application.Appointments.Risk;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Omro.Contracts.Events;

namespace MedCareOS.Application.UnitTests.Risk;

public class OmroAnomalyPublisherTests
{
    [Fact]
    public async Task PublishAsync_ShouldEmitCanonicalFinalNoticeStatusAndClinicalFlagsAlongsideExistingAttributes()
    {
        var now = DateTimeOffset.UtcNow;
        var staff = Staff.Create(Guid.NewGuid(), "Dra", "Perez", "12345678-9", "Oncologia", "Medico", "+56911111111");
        var patient = Patient.Create(Guid.NewGuid(), "98765432-1", "Juan", "Soto", new DateOnly(1980, 3, 15), "M", "+56922222222");
        var start = now.UtcDateTime.AddHours(10);
        var block = ScheduleBlock.Create(
            staff.Id,
            Guid.NewGuid(),
            start,
            start.AddHours(1),
            Guid.NewGuid(),
            DateOnly.FromDateTime(start),
            DateOnly.FromDateTime(start),
            TimeOnly.FromDateTime(start),
            TimeOnly.FromDateTime(start.AddHours(1)),
            [start.DayOfWeek]);
        var appointment = Appointment.Create(block.Id, patient.Id, "Control oncológico", "paciente");
        appointment.RecordInitialModelScore(0.88m, "Alto", HysteresisThresholds.Default, now.AddHours(-12));

        var deliveredFinal = Notification.Create(
            appointment.TenantId,
            appointment.Id,
            patient.Id,
            NotificationType.FinalConfirmation,
            NotificationChannel.Email,
            "appointment.final_confirmation",
            1,
            "es-CL",
            "{}",
            now.AddHours(-6),
            Guid.NewGuid(),
            now.AddHours(-6));
        deliveredFinal.MarkSent("Resend", "re_1", "Final", now.AddHours(-6));
        deliveredFinal.ApplyDeliveryStatus(NotificationStatus.Delivered, now.AddHours(-5));

        var policy = TenantConfirmationPolicy.CreateDefault(appointment.TenantId, now);

        var patientRepo = new Mock<IPatientRepository>();
        var blockRepo = new Mock<IScheduleBlockRepository>();
        var staffRepo = new Mock<IStaffRepository>();
        var appointmentRepo = new Mock<IAppointmentRepository>();
        appointmentRepo.Setup(r => r.GetNoShowCountByPatientId(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var notifRepo = new Mock<INotificationRepository>();
        notifRepo.Setup(r => r.GetLatestByAppointmentAndTypeAsync(
                appointment.Id,
                NotificationType.FinalConfirmation,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(deliveredFinal);

        var policyRepo = new Mock<ITenantConfirmationPolicyRepository>();
        policyRepo.Setup(r => r.GetOrDefaultAsync(appointment.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        AnomalyDetected? published = null;
        var publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .Setup(p => p.Publish(It.IsAny<AnomalyDetected>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((ev, _) => published = (AnomalyDetected)ev)
            .Returns(Task.CompletedTask);

        var publisher = new OmroAnomalyPublisher(
            patientRepo.Object,
            blockRepo.Object,
            staffRepo.Object,
            appointmentRepo.Object,
            notifRepo.Object,
            policyRepo.Object,
            publishEndpoint.Object,
            NullLogger<OmroAnomalyPublisher>.Instance);

        var result = await publisher.PublishAsync(new PublishAnomalyContext(
            Appointment: appointment,
            AnomalyType: "ConfirmationDeadlineMissed",
            CorrelationId: Guid.NewGuid(),
            OccurredAtUtc: now,
            TriggerSource: "unit-test",
            Patient: patient,
            ScheduleBlock: block,
            Staff: staff));

        result.Should().BeTrue();
        published.Should().NotBeNull();
        published!.Attributes["final_notification_delivered"].Should().Be("true");
        published.Attributes["final_notice_status"].Should().Be("Delivered");
        published.Attributes["clinical_exclusion"].Should().Be("true");
        published.Attributes["clinical_exclusion_code"].Should().Be("EXCLUDED_SPECIALTY");

        var flags = JsonSerializer.Deserialize<string[]>(published.Attributes["clinical_flags"]);
        flags.Should().BeEquivalentTo("EXCLUDED_SPECIALTY");
    }

    [Fact]
    public async Task PublishAsync_WhenNoFinalNoticeAndNoClinicalExclusion_ShouldEmitNoneAndEmptyClinicalFlags()
    {
        var now = DateTimeOffset.UtcNow;
        var staff = Staff.Create(Guid.NewGuid(), "Dr", "Vega", "11223344-5", "Medicina General", "Medico", "+56933333333");
        var patient = Patient.Create(Guid.NewGuid(), "55667788-9", "Ana", "Rojas", new DateOnly(1992, 7, 20), "F", "+56944444444");
        var start = now.UtcDateTime.AddHours(10);
        var block = ScheduleBlock.Create(
            staff.Id,
            Guid.NewGuid(),
            start,
            start.AddHours(1),
            Guid.NewGuid(),
            DateOnly.FromDateTime(start),
            DateOnly.FromDateTime(start),
            TimeOnly.FromDateTime(start),
            TimeOnly.FromDateTime(start.AddHours(1)),
            [start.DayOfWeek]);
        var appointment = Appointment.Create(block.Id, patient.Id, "Control preventivo", "paciente");
        appointment.RecordInitialModelScore(0.72m, "Alto", HysteresisThresholds.Default, now.AddHours(-12));

        var policy = TenantConfirmationPolicy.CreateDefault(appointment.TenantId, now);

        var patientRepo = new Mock<IPatientRepository>();
        var blockRepo = new Mock<IScheduleBlockRepository>();
        var staffRepo = new Mock<IStaffRepository>();
        var appointmentRepo = new Mock<IAppointmentRepository>();
        appointmentRepo.Setup(r => r.GetNoShowCountByPatientId(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var notifRepo = new Mock<INotificationRepository>();
        notifRepo.Setup(r => r.GetLatestByAppointmentAndTypeAsync(
                appointment.Id,
                NotificationType.FinalConfirmation,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Notification?)null);

        var policyRepo = new Mock<ITenantConfirmationPolicyRepository>();
        policyRepo.Setup(r => r.GetOrDefaultAsync(appointment.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        AnomalyDetected? published = null;
        var publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .Setup(p => p.Publish(It.IsAny<AnomalyDetected>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((ev, _) => published = (AnomalyDetected)ev)
            .Returns(Task.CompletedTask);

        var publisher = new OmroAnomalyPublisher(
            patientRepo.Object,
            blockRepo.Object,
            staffRepo.Object,
            appointmentRepo.Object,
            notifRepo.Object,
            policyRepo.Object,
            publishEndpoint.Object,
            NullLogger<OmroAnomalyPublisher>.Instance);

        var result = await publisher.PublishAsync(new PublishAnomalyContext(
            Appointment: appointment,
            AnomalyType: "ConfirmationDeadlineMissed",
            CorrelationId: Guid.NewGuid(),
            OccurredAtUtc: now,
            TriggerSource: "unit-test",
            Patient: patient,
            ScheduleBlock: block,
            Staff: staff));

        result.Should().BeTrue();
        published.Should().NotBeNull();
        published!.Attributes["final_notification_delivered"].Should().Be("false");
        published.Attributes["final_notice_status"].Should().Be("None");
        published.Attributes["clinical_exclusion"].Should().Be("false");
        published.Attributes["clinical_exclusion_code"].Should().Be("NONE");
        published.Attributes["clinical_flags"].Should().Be("[]");
    }
}
