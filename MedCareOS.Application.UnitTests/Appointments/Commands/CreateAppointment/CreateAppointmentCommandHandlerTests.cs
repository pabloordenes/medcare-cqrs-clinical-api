using FluentAssertions;
using MassTransit;
using MedCareOS.Application.Appointments.Commands.CreateAppointment;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Moq;

namespace MedCareOS.Application.UnitTests;

public class CreateAppointmentCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenPatientExists_CreatesAppointmentAndReturnsItsId()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var patientId = Guid.NewGuid();
        var scheduleBlockId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var patient = Patient.Create(
            Guid.NewGuid(),
            "12.345.678-9",
            "Ana",
            "Perez",
            new DateOnly(1990, 1, 1),
            "F",
            "+56912345678");
        var scheduleBlock = ScheduleBlock.Create(
            staffId,
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(1).AddMinutes(30),
            Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            new TimeOnly(9, 0),
            new TimeOnly(9, 30),
            [DateTime.UtcNow.DayOfWeek]);
        var staff = Staff.Create(
            Guid.NewGuid(),
            "Carlos",
            "Medico",
            "9.876.543-2",
            "Medicina General",
            "Doctor",
            "+56987654321");
        var command = new CreateAppointmentCommand(
            scheduleBlockId,
            patientId,
            "Dolor de cabeza",
            "paciente");
        Appointment? addedAppointment = null;

        var appointmentRepository = new Mock<IAppointmentRepository>();
        appointmentRepository
            .Setup(repository => repository.GetNoShowCountByPatientId(patientId, cancellationToken))
            .ReturnsAsync(0);
        appointmentRepository
            .Setup(repository => repository.AddAppointmentAsync(
                It.IsAny<Appointment>(),
                cancellationToken))
            .Callback<Appointment, CancellationToken>((appointment, _) => addedAppointment = appointment)
            .Returns(Task.CompletedTask);

        var patientRepository = new Mock<IPatientRepository>();
        patientRepository
            .Setup(repository => repository.GetPatientByIdAsync(patientId, cancellationToken))
            .ReturnsAsync(patient);

        var scheduleBlockRepository = new Mock<IScheduleBlockRepository>();
        scheduleBlockRepository
            .Setup(repository => repository.GetByIdAsync(scheduleBlockId, cancellationToken))
            .ReturnsAsync(scheduleBlock);

        var staffRepository = new Mock<IStaffRepository>();
        staffRepository
            .Setup(repository => repository.GetByIdAsync(staffId, cancellationToken))
            .ReturnsAsync(staff);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(work => work.SaveChangesAsync(cancellationToken))
            .ReturnsAsync(1);

        var aiClinicalService = new Mock<IAiClinicalService>();
        aiClinicalService
            .Setup(service => service.GetNlpSummaryAsync(
                It.IsAny<Guid>(),
                command.SymptomsRaw,
                cancellationToken))
            .ReturnsAsync(new NlpSummaryResponseDto(
                "Cefalea reportada",
                [new SymptomEntityDto("dolor", "cabeza", "moderado")],
                "media"));
        aiClinicalService
            .Setup(service => service.PredictNoShowRiskAsync(
                It.IsAny<PredictRequestDto>(),
                cancellationToken))
            .ReturnsAsync(new PredictResponseDto(
                Guid.NewGuid(),
                0.2m,
                "Bajo",
                "no"));

        var publishEndpoint = new Mock<IPublishEndpoint>();
        var handler = new CreateAppointmentCommandHandler(
            appointmentRepository.Object,
            patientRepository.Object,
            unitOfWork.Object,
            scheduleBlockRepository.Object,
            staffRepository.Object,
            aiClinicalService.Object,
            publishEndpoint.Object);

        // Act
        var result = await handler.Handle(command, cancellationToken);

        // Assert
        result.Should().NotBe(Guid.Empty);
        addedAppointment.Should().NotBeNull();
        addedAppointment!.Id.Should().Be(result);
        addedAppointment.PatientId.Should().Be(patientId);
        addedAppointment.ScheduleBlockId.Should().Be(scheduleBlockId);
        addedAppointment.NlpSummary.Should().Be("Cefalea reportada");
        addedAppointment.NoShowRiskScore.Should().Be(0.2m);
        addedAppointment.RiskBand.Should().Be("Bajo");
        appointmentRepository.Verify(
            repository => repository.AddAppointmentAsync(
                It.IsAny<Appointment>(),
                cancellationToken),
            Times.Once);
        unitOfWork.Verify(work => work.SaveChangesAsync(cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPatientDoesNotExist_ThrowsInvalidOperationException()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var patientId = Guid.NewGuid();
        var command = new CreateAppointmentCommand(
            Guid.NewGuid(),
            patientId,
            "Dolor de cabeza",
            "paciente");
        var patientRepository = new Mock<IPatientRepository>();
        patientRepository
            .Setup(repository => repository.GetPatientByIdAsync(patientId, cancellationToken))
            .ReturnsAsync((Patient?)null);
        var scheduleBlockRepository = new Mock<IScheduleBlockRepository>();
        var handler = new CreateAppointmentCommandHandler(
            Mock.Of<IAppointmentRepository>(),
            patientRepository.Object,
            Mock.Of<IUnitOfWork>(),
            scheduleBlockRepository.Object,
            Mock.Of<IStaffRepository>(),
            Mock.Of<IAiClinicalService>(),
            Mock.Of<IPublishEndpoint>());

        // Act
        Func<Task> act = () => handler.Handle(command, cancellationToken);

        // Assert
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("El paciente no existe.");
        scheduleBlockRepository.Verify(
            repository => repository.GetByIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
