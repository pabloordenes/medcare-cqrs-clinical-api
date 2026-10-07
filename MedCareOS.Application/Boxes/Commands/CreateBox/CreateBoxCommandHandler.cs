using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Boxes.Commands.CreateBox;

public class CreateBoxCommandHandler : IRequestHandler<CreateBoxCommand, Guid>
{
    private readonly IBoxRepository _boxRepository;
    private readonly IUnitOfWork _unitOfWork;
    
    public  CreateBoxCommandHandler(IBoxRepository boxRepository, IUnitOfWork unitOfWork)
    {
        _boxRepository = boxRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreateBoxCommand request, CancellationToken cancellationToken)
    {
        bool existsByNameAndFloor = await _boxRepository.ExistsByNameAndFloorAsync(request.Name, request.Floor, cancellationToken);

        if (existsByNameAndFloor)
            throw new Exception($"Ya existe un box con el nombre {request.Name} en el piso {request.Floor}.");
        
        var newBox = Box.Create(
            request.Name,
            request.Type,
            request.Capacity,
            request.Floor
        );
        
        _boxRepository.Add(newBox);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        return newBox.Id;
    }
}