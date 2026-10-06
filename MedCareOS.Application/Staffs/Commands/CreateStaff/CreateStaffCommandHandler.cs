using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Staffs.Commands.CreateStaff;

public class CreateStaffCommandHandler : IRequestHandler<CreateStaffCommand, Guid>
{
    private readonly IStaffRepository _staffRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateStaffCommandHandler(IStaffRepository staffRepository, IUserRepository userRepository, IUnitOfWork unitOfWork)
    {
        _staffRepository = staffRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreateStaffCommand request, CancellationToken cancellationToken)
    {
        var staffRut = await _staffRepository.FindByRutAsync(request.Rut, cancellationToken);

        if (staffRut != null)
            throw new Exception("El RUT ingresado ya existe."); // todo: implementar middleware
        
        var staff = await _staffRepository.GetByUserIdAsync(request.UserId, cancellationToken);
        
        if (staff != null)
            throw new Exception("El staff ya tiene un usuario vinculado."); // middleware
        
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        
        if (user == null)
        {
            var existsByEmail = await _userRepository.ExistsByEmailAsync(request.Email, cancellationToken);
            
            if (existsByEmail)
            {
                throw new Exception("El correo ingresado ya esta registrado.");
            }

            var newUser = User.Create(
                request.UserId,
                request.Email,
                request.Role
            );
            
            _userRepository.AddUser(newUser);
        }

        var newStaff = Staff.Create(
            request.UserId,
            request.FirstName,
            request.LastName,
            request.Rut,
            request.Specialty,
            request.RoleName,
            request.Phone
        );
        
        _staffRepository.AddStaff(newStaff);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        return newStaff.Id;
    }
}