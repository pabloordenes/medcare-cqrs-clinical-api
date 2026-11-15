using FluentAssertions;
using MassTransit;
using MedCareOS.Application.Appointments.Commands.CreateAppointment;
using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace MedCareOS.Application.UnitTests;

public class CreateAppointmentCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenPatientExists_CreatesAppointmentAndReturnsItsId_FastAndFireAndForget()
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

        var appointmentRepository = new Mock<IAppointmentRepository>();
        appointmentRepository
            .Setup(repository => repository.AddAppointmentAsync(It.IsAny<Appointment>(), cancellationToken))
            .Callback<Appointment, CancellationToken>((appointment, _) => addedAppointment = appointment)
            .Returns(Task.CompletedTask);

        var patientRepository = new Mock<IPatientRepository>();
        patientRepository.Setup(r => r.GetPatientByIdAsync(patientId, cancellationToken)).ReturnsAsync(patient);

        var scheduleBlockRepository = new Mock<IScheduleBlockRepository>();
        scheduleBlockRepository.Setup(r => r.GetByIdAsync(scheduleBlockId, cancellationToken)).ReturnsAsync(scheduleBlock);

        var staffRepository = new Mock<IStaffRepository>();
        staffRepository.Setup(r => r.GetByIdAsync(staffId, cancellationToken)).ReturnsAsync(staff);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(w => w.SaveChangesAsync(cancellationToken)).ReturnsAsync(1);

        // --- Configuración del IServiceScopeFactory Mock (Para el hilo en Background) ---
        var serviceProvider = new Mock<IServiceProvider>();
        
        // Mock de las dependencias que el hilo interno pedirá
        serviceProvider.Setup(x => x.GetService(typeof(IAiClinicalService))).Returns(Mock.Of<IAiClinicalService>());
        serviceProvider.Setup(x => x.GetService(typeof(IAppointmentRepository))).Returns(Mock.Of<IAppointmentRepository>());
        serviceProvider.Setup(x => x.GetService(typeof(IUnitOfWork))).Returns(Mock.Of<IUnitOfWork>());
        serviceProvider.Setup(x => x.GetService(typeof(IPublishEndpoint))).Returns(Mock.Of<IPublishEndpoint>());
        serviceProvider.Setup(x => x.GetService(typeof(ILogger<CreateAppointmentCommandHandler>)))
            .Returns(Mock.Of<ILogger<CreateAppointmentCommandHandler>>());

        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(x => x.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(x => x.CreateScope()).Returns(serviceScope.Object);
        // --------------------------------------------------------------------------------

        var handler = new CreateAppointmentCommandHandler(
            appointmentRepository.Object,
            patientRepository.Object,
            unitOfWork.Object,
            scheduleBlockRepository.Object,
            staffRepository.Object,
            Mock.Of<IAiClinicalService>(), // <- Añadido
            Mock.Of<IPublishEndpoint>(),   // <- Añadido
            scopeFactory.Object);

        // Act
        var result = await handler.Handle(command, cancellationToken);

        // Assert (Transacción inmediata)
        result.Should().NotBe(Guid.Empty);
        addedAppointment.Should().NotBeNull();
        addedAppointment!.Id.Should().Be(result);
        addedAppointment.PatientId.Should().Be(patientId);
        addedAppointment.ScheduleBlockId.Should().Be(scheduleBlockId);
        
        // Verificamos que se guardó en la DB la fase 1
        appointmentRepository.Verify(r => r.AddAppointmentAsync(It.IsAny<Appointment>(), cancellationToken), Times.Once);
        unitOfWork.Verify(w => w.SaveChangesAsync(cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPatientDoesNotExist_ThrowsInvalidOperationException()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var patientId = Guid.NewGuid();
        var command = new CreateAppointmentCommand(Guid.NewGuid(), patientId, "Dolor", "paciente");
        
        var patientRepository = new Mock<IPatientRepository>();
        patientRepository.Setup(r => r.GetPatientByIdAsync(patientId, cancellationToken)).ReturnsAsync((Patient?)null);
        
        var handler = new CreateAppointmentCommandHandler(
            Mock.Of<IAppointmentRepository>(),
            patientRepository.Object,
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IScheduleBlockRepository>(),
            Mock.Of<IStaffRepository>(),
            Mock.Of<IAiClinicalService>(), // <- Añadido
            Mock.Of<IPublishEndpoint>(),   // <- Añadido
            Mock.Of<IServiceScopeFactory>()); // Dummy inyectado

        // Act
        Func<Task> act = () => handler.Handle(command, cancellationToken);

        // Assert
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("El paciente no existe.");
    }
}