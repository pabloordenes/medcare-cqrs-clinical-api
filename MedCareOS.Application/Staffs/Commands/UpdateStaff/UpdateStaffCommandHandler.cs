using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Staffs.Commands.UpdateStaff;

public class UpdateStaffCommandHandler : IRequestHandler<UpdateStaffCommand>
{
    private readonly IStaffRepository _staffRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateStaffCommandHandler(IStaffRepository staffRepository, IUserRepository userRepository,
        IUnitOfWork unitOfWork)
    {
        _staffRepository = staffRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(UpdateStaffCommand request, CancellationToken cancellationToken)
    {
        var staff = await _staffRepository.GetByIdAsync(request.StaffId, cancellationToken);

        if (staff == null)
            throw new Exception("Trabajador no encontrado.");

        var user = await _userRepository.GetByIdAsync(staff.UserId, cancellationToken);

        if (user == null)
            throw new Exception("Usuario no encontrado.");
        
        staff.UpdateProfile(
            request.FirstName,
            request.LastName,
            request.Rut,
            request.Phone,
            request.Specialty,
            request.RoleName);
        
        user.ChangeRole(request.Role);

        if (request.IsActive)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
        }
        
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}