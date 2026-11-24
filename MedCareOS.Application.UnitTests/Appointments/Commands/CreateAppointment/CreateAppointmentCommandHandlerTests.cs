using FluentAssertions;
using MassTransit;
using MedCareOS.Application.Appointments.Commands.CreateAppointment;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Exceptions;
using MedCareOS.Domain.Repositories;
using Moq;

namespace MedCareOS.Application.UnitTests;

public class CreateAppointmentCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenValid_PersistsAppointmentAndPublishesOutboxEventInSameTransaction()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var patientId = Guid.NewGuid();
        var scheduleBlockId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var patient = Patient.Create(
            Guid.NewGuid(), "12.345.678-9", "Ana", "Perez", new DateOnly(1990, 1, 1), "F", "+56912345678");
        var scheduleBlock = ScheduleBlock.Create(
            staffId, Guid.NewGuid(), DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddMinutes(30),
            Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            new TimeOnly(9, 0), new TimeOnly(9, 30), [DateTime.UtcNow.DayOfWeek]);
        var staff = Staff.Create(
            Guid.NewGuid(), "Carlos", "Medico", "9.876.543-2", "Medicina General", "Doctor", "+56987654321");

        var command = new CreateAppointmentCommand(scheduleBlockId, patientId, "Dolor de cabeza", "paciente");
        Appointment? addedAppointment = null;
        AppointmentCreated? publishedEvent = null;

        var sequence = new MockSequence();
        var appointmentRepository = new Mock<IAppointmentRepository>(MockBehavior.Loose);
        appointmentRepository
            .Setup(r => r.HasActiveAppointmentForBlockAsync(scheduleBlockId, cancellationToken))
            .ReturnsAsync(false);
        appointmentRepository
            .InSequence(sequence)
            .Setup(r => r.AddAppointmentAsync(It.IsAny<Appointment>(), cancellationToken))
            .Callback<Appointment, CancellationToken>((appointment, _) => addedAppointment = appointment)
            .Returns(Task.CompletedTask);

        var patientRepository = new Mock<IPatientRepository>();
        patientRepository.Setup(r => r.GetPatientByIdAsync(patientId, cancellationToken)).ReturnsAsync(patient);

        var scheduleBlockRepository = new Mock<IScheduleBlockRepository>();
        scheduleBlockRepository.Setup(r => r.GetByIdAsync(scheduleBlockId, cancellationToken)).ReturnsAsync(scheduleBlock);

        var staffRepository = new Mock<IStaffRepository>();
        staffRepository.Setup(r => r.GetByIdAsync(staffId, cancellationToken)).ReturnsAsync(staff);

        var publishEndpoint = new Mock<IPublishEndpoint>();
        publishEndpoint
            .InSequence(sequence)
            .Setup(p => p.Publish(It.IsAny<AppointmentCreated>(), cancellationToken))
            .Callback<AppointmentCreated, CancellationToken>((evt, _) => publishedEvent = evt)
            .Returns(Task.CompletedTask);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .InSequence(sequence)
            .Setup(w => w.SaveChangesAsync(cancellationToken))
            .ReturnsAsync(1);

        var handler = new CreateAppointmentCommandHandler(
            appointmentRepository.Object,
            patientRepository.Object,
            unitOfWork.Object,
            scheduleBlockRepository.Object,
            staffRepository.Object,
            publishEndpoint.Object);

        // Act
        var result = await handler.Handle(command, cancellationToken);

        // Assert
        result.Should().NotBe(Guid.Empty);
        addedAppointment.Should().NotBeNull();
        addedAppointment!.Id.Should().Be(result);
        addedAppointment.PatientId.Should().Be(patientId);
        addedAppointment.ScheduleBlockId.Should().Be(scheduleBlockId);

        publishedEvent.Should().NotBeNull();
        publishedEvent!.AppointmentId.Should().Be(result);
        publishedEvent.CorrelationId.Should().NotBe(Guid.Empty);

        appointmentRepository.Verify(r => r.AddAppointmentAsync(It.IsAny<Appointment>(), cancellationToken), Times.Once);
        publishEndpoint.Verify(p => p.Publish(It.IsAny<AppointmentCreated>(), cancellationToken), Times.Once);
        unitOfWork.Verify(w => w.SaveChangesAsync(cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPatientDoesNotExist_ThrowsInvalidOperationExceptionAndPublishesNothing()
    {
        var cancellationToken = CancellationToken.None;
        var patientId = Guid.NewGuid();
        var command = new CreateAppointmentCommand(Guid.NewGuid(), patientId, "Dolor", "paciente");

        var patientRepository = new Mock<IPatientRepository>();
        patientRepository.Setup(r => r.GetPatientByIdAsync(patientId, cancellationToken)).ReturnsAsync((Patient?)null);

        var publishEndpoint = new Mock<IPublishEndpoint>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new CreateAppointmentCommandHandler(
            Mock.Of<IAppointmentRepository>(),
            patientRepository.Object,
            unitOfWork.Object,
            Mock.Of<IScheduleBlockRepository>(),
            Mock.Of<IStaffRepository>(),
            publishEndpoint.Object);

        Func<Task> act = () => handler.Handle(command, cancellationToken);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("El paciente no existe.");
        publishEndpoint.Verify(p => p.Publish(It.IsAny<AppointmentCreated>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenScheduleBlockAlreadyOccupied_ThrowsDuplicateAppointmentExceptionAndPublishesNothing()
    {
        var cancellationToken = CancellationToken.None;
        var patientId = Guid.NewGuid();
        var scheduleBlockId = Guid.NewGuid();
        var patient = Patient.Create(
            Guid.NewGuid(), "12.345.678-9", "Ana", "Perez", new DateOnly(1990, 1, 1), "F", "+56912345678");
        var scheduleBlock = ScheduleBlock.Create(
            Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddMinutes(30),
            Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            new TimeOnly(9, 0), new TimeOnly(9, 30), [DateTime.UtcNow.DayOfWeek]);

        var appointmentRepository = new Mock<IAppointmentRepository>();
        appointmentRepository
            .Setup(r => r.HasActiveAppointmentForBlockAsync(scheduleBlockId, cancellationToken))
            .ReturnsAsync(true);

        var patientRepository = new Mock<IPatientRepository>();
        patientRepository.Setup(r => r.GetPatientByIdAsync(patientId, cancellationToken)).ReturnsAsync(patient);

        var scheduleBlockRepository = new Mock<IScheduleBlockRepository>();
        scheduleBlockRepository.Setup(r => r.GetByIdAsync(scheduleBlockId, cancellationToken)).ReturnsAsync(scheduleBlock);

        var publishEndpoint = new Mock<IPublishEndpoint>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new CreateAppointmentCommandHandler(
            appointmentRepository.Object,
            patientRepository.Object,
            unitOfWork.Object,
            scheduleBlockRepository.Object,
            Mock.Of<IStaffRepository>(),
            publishEndpoint.Object);

        var command = new CreateAppointmentCommand(scheduleBlockId, patientId, "Dolor", "paciente");
        Func<Task> act = () => handler.Handle(command, cancellationToken);

        await act.Should().ThrowAsync<DuplicateAppointmentException>();
        publishEndpoint.Verify(p => p.Publish(It.IsAny<AppointmentCreated>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
