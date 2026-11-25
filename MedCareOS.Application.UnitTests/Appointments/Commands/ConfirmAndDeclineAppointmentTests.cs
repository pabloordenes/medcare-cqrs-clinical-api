using FluentAssertions;
using MassTransit;
using MedCareOS.Application.Appointments.Commands.ConfirmAppointment;
using MedCareOS.Application.Appointments.Commands.DeclineAppointment;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Application.Appointments.Risk;
using MedCareOS.Application.Notifications.Events;
using MedCareOS.Application.Resolutions.Services;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MedCareOS.Application.UnitTests.Appointments.Commands;

public class ConfirmAndDeclineAppointmentTests
{
    [Fact]
    public async Task ConfirmHandle_WhenPending_ConfirmedEarly_RecordsSignal_CancelsActions_SupersedesHitl_AndPublishesBeforeSave()
    {
        var ct = CancellationToken.None;
        var patientId = Guid.NewGuid();
        var appointment = Appointment.Create(Guid.NewGuid(), patientId, "Control", "paciente");
        var policy = TenantConfirmationPolicy.CreateDefault(appointment.TenantId);
        var now = DateTimeOffset.UtcNow;
        appointment.RecordInitialModelScore(0.75m, "Alto", policy.GetHysteresisThresholds(), now);

        var appointmentRepo = new Mock<IAppointmentRepository>();
        appointmentRepo.Setup(r => r.GetAppointmentByIdAsync(appointment.Id, ct)).ReturnsAsync(appointment);

        var lifecyclePlanner = new Mock<IAppointmentLifecyclePlanner>();
        lifecyclePlanner
            .Setup(p => p.CancelPendingActionsAsync(appointment.Id, "PatientConfirmedEarly", now, ct))
            .ReturnsAsync(2);

        var riskSignalProcessor = new Mock<IRiskSignalProcessor>();
        riskSignalProcessor
            .Setup(r => r.RecordAsync(It.IsAny<RecordRiskSignalRequest>(), ct))
            .ReturnsAsync(new RiskSignalApplicationResult(true, false, Guid.NewGuid(), 0.75m, 0.40m, RiskBandLevel.High, RiskBandLevel.Medium));

        var supersedingService = new Mock<IAiResolutionSupersedingService>();
        supersedingService
            .Setup(s => s.SupersedePendingForResourceAsync(appointment.Id, "PatientConfirmedEarly", ct))
            .ReturnsAsync(1);

        var sequence = new MockSequence();
        var publishEndpoint = new Mock<IPublishEndpoint>();
        var unitOfWork = new Mock<IUnitOfWork>();

        publishEndpoint
            .InSequence(sequence)
            .Setup(p => p.Publish(It.IsAny<PatientAppointmentReplyReceived>(), ct))
            .Returns(Task.CompletedTask);

        unitOfWork
            .InSequence(sequence)
            .Setup(u => u.SaveChangesAsync(ct))
            .ReturnsAsync(1);

        var handler = new ConfirmAppointmentCommandHandler(
            appointmentRepo.Object,
            lifecyclePlanner.Object,
            riskSignalProcessor.Object,
            supersedingService.Object,
            publishEndpoint.Object,
            unitOfWork.Object,
            NullLogger<ConfirmAppointmentCommandHandler>.Instance);

        var notificationId = Guid.NewGuid();
        var result = await handler.Handle(
            new ConfirmAppointmentCommand(
                AppointmentId: appointment.Id,
                ExpectedPatientId: patientId,
                Channel: NotificationChannel.WhatsApp,
                NotificationId: notificationId,
                Source: "whatsapp_inbound",
                NormalizedKeyword: "SI",
                OccurredAtUtc: now,
                PublishPatientReplyEvent: true),
            ct);

        result.Outcome.Should().Be(ConfirmationTransitionOutcome.ConfirmedEarly);
        result.AppointmentStatus.Should().Be(AppointmentStatus.Confirmed);
        result.ConfirmationState.Should().Be(ConfirmationState.ConfirmedEarly);
        result.StateChanged.Should().BeTrue();
        result.WasIdempotentReplay.Should().BeFalse();
        result.CancelledScheduledActionsCount.Should().Be(2);
        result.SupersededHitlTasksCount.Should().Be(1);

        appointment.OrchestrationSuppressed.Should().BeTrue();

        riskSignalProcessor.Verify(r => r.RecordAsync(
            It.Is<RecordRiskSignalRequest>(req =>
                req.AppointmentId == appointment.Id &&
                req.Kind == RiskSignalKind.ConfirmedEarly &&
                req.IdempotencyKey == $"confirmed_early:{appointment.Id:N}" &&
                req.Source == "whatsapp_inbound"),
            ct), Times.Once);

        lifecyclePlanner.Verify(p => p.CancelPendingActionsAsync(appointment.Id, "PatientConfirmedEarly", now, ct), Times.Once);
        supersedingService.Verify(s => s.SupersedePendingForResourceAsync(appointment.Id, "PatientConfirmedEarly", ct), Times.Once);
        publishEndpoint.Verify(p => p.Publish(
            It.Is<PatientAppointmentReplyReceived>(e =>
                e.AppointmentId == appointment.Id &&
                e.PatientId == patientId &&
                e.NotificationId == notificationId &&
                e.Intent == PatientReplyIntent.Confirm &&
                e.Channel == NotificationChannel.WhatsApp),
            ct), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(ct), Times.Once);
    }

    [Fact]
    public async Task ConfirmHandle_WhenFinalRequested_FinalConfirmed_SuppressesOrchestrationAndRecordsFinalConfirmedSignal()
    {
        var ct = CancellationToken.None;
        var patientId = Guid.NewGuid();
        var appointment = Appointment.Create(Guid.NewGuid(), patientId, "Control", "paciente");
        var now = DateTimeOffset.UtcNow;
        appointment.MarkFinalConfirmationRequested(now.AddHours(-2));

        var appointmentRepo = new Mock<IAppointmentRepository>();
        appointmentRepo.Setup(r => r.GetAppointmentByIdAsync(appointment.Id, ct)).ReturnsAsync(appointment);

        var lifecyclePlanner = new Mock<IAppointmentLifecyclePlanner>();
        lifecyclePlanner
            .Setup(p => p.CancelPendingActionsAsync(appointment.Id, "PatientFinalConfirmed", now, ct))
            .ReturnsAsync(1);

        var riskSignalProcessor = new Mock<IRiskSignalProcessor>();
        riskSignalProcessor
            .Setup(r => r.RecordAsync(It.IsAny<RecordRiskSignalRequest>(), ct))
            .ReturnsAsync(new RiskSignalApplicationResult(true, false));

        var supersedingService = new Mock<IAiResolutionSupersedingService>();
        supersedingService
            .Setup(s => s.SupersedePendingForResourceAsync(appointment.Id, "PatientFinalConfirmed", ct))
            .ReturnsAsync(1);

        var publishEndpoint = new Mock<IPublishEndpoint>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new ConfirmAppointmentCommandHandler(
            appointmentRepo.Object,
            lifecyclePlanner.Object,
            riskSignalProcessor.Object,
            supersedingService.Object,
            publishEndpoint.Object,
            unitOfWork.Object,
            NullLogger<ConfirmAppointmentCommandHandler>.Instance);

        var result = await handler.Handle(
            new ConfirmAppointmentCommand(
                AppointmentId: appointment.Id,
                ExpectedPatientId: patientId,
                Channel: NotificationChannel.Email,
                Source: "public_token",
                OccurredAtUtc: now,
                PublishPatientReplyEvent: false),
            ct);

        result.Outcome.Should().Be(ConfirmationTransitionOutcome.FinalConfirmed);
        result.ConfirmationState.Should().Be(ConfirmationState.FinalConfirmed);
        appointment.OrchestrationSuppressed.Should().BeTrue();

        riskSignalProcessor.Verify(r => r.RecordAsync(
            It.Is<RecordRiskSignalRequest>(req =>
                req.Kind == RiskSignalKind.FinalConfirmed &&
                req.IdempotencyKey == $"final_confirmed:{appointment.Id:N}"),
            ct), Times.Once);
        publishEndpoint.Verify(p => p.Publish(It.IsAny<PatientAppointmentReplyReceived>(), ct), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(ct), Times.Once);
    }

    [Fact]
    public async Task ConfirmHandle_WhenAlreadyConfirmed_AndWrongPatient_BehavesIdempotentlyAndValidatesOwnership()
    {
        var ct = CancellationToken.None;
        var patientId = Guid.NewGuid();
        var appointment = Appointment.Create(Guid.NewGuid(), patientId, "Control", "paciente");
        appointment.RegisterPatientConfirmation(DateTimeOffset.UtcNow.AddMinutes(-30));

        var appointmentRepo = new Mock<IAppointmentRepository>();
        appointmentRepo.Setup(r => r.GetAppointmentByIdAsync(appointment.Id, ct)).ReturnsAsync(appointment);

        var lifecyclePlanner = new Mock<IAppointmentLifecyclePlanner>();
        lifecyclePlanner.Setup(p => p.CancelPendingActionsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), ct)).ReturnsAsync(0);

        var riskSignalProcessor = new Mock<IRiskSignalProcessor>();
        var supersedingService = new Mock<IAiResolutionSupersedingService>();
        supersedingService.Setup(s => s.SupersedePendingForResourceAsync(It.IsAny<Guid>(), It.IsAny<string>(), ct)).ReturnsAsync(0);

        var publishEndpoint = new Mock<IPublishEndpoint>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new ConfirmAppointmentCommandHandler(
            appointmentRepo.Object,
            lifecyclePlanner.Object,
            riskSignalProcessor.Object,
            supersedingService.Object,
            publishEndpoint.Object,
            unitOfWork.Object,
            NullLogger<ConfirmAppointmentCommandHandler>.Instance);

        var replay = await handler.Handle(
            new ConfirmAppointmentCommand(appointment.Id, ExpectedPatientId: patientId, PublishPatientReplyEvent: true),
            ct);

        replay.Outcome.Should().Be(ConfirmationTransitionOutcome.AlreadyConfirmed);
        replay.StateChanged.Should().BeFalse();
        replay.WasIdempotentReplay.Should().BeTrue();
        riskSignalProcessor.Verify(r => r.RecordAsync(It.IsAny<RecordRiskSignalRequest>(), ct), Times.Never);
        publishEndpoint.Verify(p => p.Publish(It.IsAny<PatientAppointmentReplyReceived>(), ct), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(ct), Times.Never);

        Func<Task> wrongPatientAct = () => handler.Handle(
            new ConfirmAppointmentCommand(appointment.Id, ExpectedPatientId: Guid.NewGuid()),
            ct);
        await wrongPatientAct.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeclineHandle_WhenScheduled_CancelsAppointment_RecordsSignal_CancelsActions_SupersedesHitl_AndIsIdempotent()
    {
        var ct = CancellationToken.None;
        var patientId = Guid.NewGuid();
        var appointment = Appointment.Create(Guid.NewGuid(), patientId, "Control", "paciente");
        var now = DateTimeOffset.UtcNow;

        var appointmentRepo = new Mock<IAppointmentRepository>();
        appointmentRepo.Setup(r => r.GetAppointmentByIdAsync(appointment.Id, ct)).ReturnsAsync(appointment);

        var lifecyclePlanner = new Mock<IAppointmentLifecyclePlanner>();
        lifecyclePlanner.Setup(p => p.CancelPendingActionsAsync(appointment.Id, "PatientDeclined", It.IsAny<DateTimeOffset?>(), ct)).ReturnsAsync(3);

        var riskSignalProcessor = new Mock<IRiskSignalProcessor>();
        riskSignalProcessor.Setup(r => r.RecordAsync(It.IsAny<RecordRiskSignalRequest>(), ct))
            .ReturnsAsync(new RiskSignalApplicationResult(true, false));

        var supersedingService = new Mock<IAiResolutionSupersedingService>();
        supersedingService.Setup(s => s.SupersedePendingForResourceAsync(appointment.Id, "PatientDeclined", ct)).ReturnsAsync(2);

        var publishEndpoint = new Mock<IPublishEndpoint>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new DeclineAppointmentCommandHandler(
            appointmentRepo.Object,
            lifecyclePlanner.Object,
            riskSignalProcessor.Object,
            supersedingService.Object,
            publishEndpoint.Object,
            unitOfWork.Object,
            NullLogger<DeclineAppointmentCommandHandler>.Instance);

        var first = await handler.Handle(
            new DeclineAppointmentCommand(
                AppointmentId: appointment.Id,
                ExpectedPatientId: patientId,
                Channel: NotificationChannel.Email,
                Source: "public_token",
                Reason: "No puedo asistir",
                NormalizedKeyword: "LINK_CANCEL",
                OccurredAtUtc: now,
                PublishPatientReplyEvent: true),
            ct);

        first.Outcome.Should().Be(ConfirmationTransitionOutcome.DeclinedByPatient);
        first.AppointmentStatus.Should().Be(AppointmentStatus.Cancelled);
        first.ConfirmationState.Should().Be(ConfirmationState.DeclinedByPatient);
        first.StateChanged.Should().BeTrue();
        first.WasIdempotentReplay.Should().BeFalse();
        first.CancelledScheduledActionsCount.Should().Be(3);
        first.SupersededHitlTasksCount.Should().Be(2);

        riskSignalProcessor.Verify(r => r.RecordAsync(
            It.Is<RecordRiskSignalRequest>(req =>
                req.Kind == RiskSignalKind.DeclinedByPatient &&
                req.IdempotencyKey == $"declined_by_patient:{appointment.Id:N}"),
            ct), Times.Once);
        publishEndpoint.Verify(p => p.Publish(
            It.Is<PatientAppointmentReplyReceived>(e =>
                e.AppointmentId == appointment.Id &&
                e.Intent == PatientReplyIntent.Cancel),
            ct), Times.Once);

        // Idempotent replay
        lifecyclePlanner.Setup(p => p.CancelPendingActionsAsync(appointment.Id, "PatientDeclined", It.IsAny<DateTimeOffset?>(), ct)).ReturnsAsync(0);
        supersedingService.Setup(s => s.SupersedePendingForResourceAsync(appointment.Id, "PatientDeclined", ct)).ReturnsAsync(0);

        var replay = await handler.Handle(
            new DeclineAppointmentCommand(appointment.Id, ExpectedPatientId: patientId, PublishPatientReplyEvent: true),
            ct);

        replay.Outcome.Should().Be(ConfirmationTransitionOutcome.AlreadyDeclined);
        replay.WasIdempotentReplay.Should().BeTrue();
        riskSignalProcessor.Verify(r => r.RecordAsync(It.IsAny<RecordRiskSignalRequest>(), ct), Times.Once);
        publishEndpoint.Verify(p => p.Publish(It.IsAny<PatientAppointmentReplyReceived>(), ct), Times.Once);

        Func<Task> wrongPatientAct = () => handler.Handle(
            new DeclineAppointmentCommand(appointment.Id, ExpectedPatientId: Guid.NewGuid()),
            ct);
        await wrongPatientAct.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ConfirmHandle_WhenUnresponsive_LateConfirmation_PublishesLateConfirmationReceived()
    {
        var ct = CancellationToken.None;
        var patientId = Guid.NewGuid();
        var appointment = Appointment.Create(Guid.NewGuid(), patientId, "Control", "paciente");
        var now = DateTimeOffset.UtcNow;
        appointment.MarkFinalConfirmationRequested(now.AddHours(-12));
        appointment.MarkUnresponsive(now.AddMinutes(-30));

        var appointmentRepo = new Mock<IAppointmentRepository>();
        appointmentRepo.Setup(r => r.GetAppointmentByIdAsync(appointment.Id, ct)).ReturnsAsync(appointment);

        var lifecyclePlanner = new Mock<IAppointmentLifecyclePlanner>();
        lifecyclePlanner.Setup(p => p.CancelPendingActionsAsync(appointment.Id, "PatientFinalConfirmed", now, ct)).ReturnsAsync(0);

        var riskSignalProcessor = new Mock<IRiskSignalProcessor>();
        riskSignalProcessor
            .Setup(r => r.RecordAsync(It.IsAny<RecordRiskSignalRequest>(), ct))
            .ReturnsAsync(new RiskSignalApplicationResult(true, false));

        var supersedingService = new Mock<IAiResolutionSupersedingService>();
        supersedingService.Setup(s => s.SupersedePendingForResourceAsync(appointment.Id, "PatientFinalConfirmed", ct)).ReturnsAsync(0);

        var publishEndpoint = new Mock<IPublishEndpoint>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new ConfirmAppointmentCommandHandler(
            appointmentRepo.Object,
            lifecyclePlanner.Object,
            riskSignalProcessor.Object,
            supersedingService.Object,
            publishEndpoint.Object,
            unitOfWork.Object,
            NullLogger<ConfirmAppointmentCommandHandler>.Instance);

        var correlationId = Guid.NewGuid();
        var result = await handler.Handle(
            new ConfirmAppointmentCommand(
                AppointmentId: appointment.Id,
                ExpectedPatientId: patientId,
                OccurredAtUtc: now,
                CorrelationId: correlationId),
            ct);

        result.Outcome.Should().Be(ConfirmationTransitionOutcome.FinalConfirmed);
        publishEndpoint.Verify(p => p.Publish(
            It.Is<LateConfirmationReceived>(e =>
                e.AppointmentId == appointment.Id &&
                e.TenantId == appointment.TenantId &&
                e.ConfirmedAtUtc == now &&
                e.CorrelationId == correlationId),
            ct), Times.Once);
    }
}
