using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Boxes.Commands.UpdateBox;

public class UpdateBoxCommandHandler : IRequestHandler<UpdateBoxCommand>
{
    private readonly IBoxRepository _boxRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateBoxCommandHandler(IBoxRepository boxRepository, IUnitOfWork unitOfWork)
    {
        _boxRepository = boxRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(UpdateBoxCommand request, CancellationToken cancellationToken)
    {
        var box = await _boxRepository.GetByIdAsync(request.BoxId, cancellationToken);
        var existsByNameAndFloorExceptId = await _boxRepository.ExistsByNameAndFloorExceptIdAsync(request.BoxId, request.Name, request.Floor, cancellationToken);

        if (box == null)
            throw new Exception("Box no encontrado.");
        
        if (existsByNameAndFloorExceptId)
            throw new Exception("Ya existe otro box con el mismo nombre y piso.");
        
        box.UpdateDetails(request.Name, request.Type, request.Capacity, request.Floor, request.IsActive);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
    
}