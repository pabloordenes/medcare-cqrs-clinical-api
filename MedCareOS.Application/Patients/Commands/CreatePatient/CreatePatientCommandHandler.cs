using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Patients.Commands.CreatePatient;

public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePatientCommandHandler(IPatientRepository patientRepository,
    IUserRepository userRepository, IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreatePatientCommand request, CancellationToken cancellationToken)
    {
        var existsByRut = await _patientRepository.ExistsByRutAsync(request.Rut, cancellationToken);

        if (existsByRut)
            throw new Exception("El paciente con el RUT proporcionado ya existe.");
        
        var patient = await _patientRepository.GetByUserId(request.UserId, cancellationToken);
        
        if (patient != null)
            throw new Exception("El paciente ya tiene un usuario vinculado.");
        
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);

        if (user == null)
        {
            var existsByEmail = await _userRepository.ExistsByEmailAsync(request.Email, cancellationToken);
            
            if (existsByEmail)
                throw new Exception("El correo ingresado ya está registrado.");

            var newUser = User.Create(
                request.UserId,
                request.Email,
                Domain.Enums.UserRole.Paciente
                );
            
            _userRepository.Add(newUser);
        }
        
        var newPatient = Patient.Create(
            request.UserId,
            request.Rut,
            request.FirstName,
            request.LastName,
            request.BirthDate,
            request.Gender,
            request.Phone,
            request.Email,
            request.Address,
            request.Neighbourhood
        );

        _patientRepository.Add(newPatient);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        return newPatient.Id;
    }
}