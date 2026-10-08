using MedCareOS.Application.Boxes.Commands.CreateBox;
using MedCareOS.Application.Boxes.Commands.UpdateBox;
using MedCareOS.Application.Boxes.Queries.GetAllBoxes;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BoxesController : ControllerBase
{
    private readonly ISender _sender;

    public BoxesController(ISender sender)
    {
        _sender = sender;
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateBox([FromBody] CreateBoxCommand command, CancellationToken cancellationToken)
    {
        var boxId =  await _sender.Send(command, cancellationToken);
        
        return StatusCode(StatusCodes.Status201Created, new { id = boxId });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetAllBoxes(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetAllBoxesQuery(), cancellationToken);
        return Ok(result);
    }
    
    [Authorize]
    [HttpPut("{boxId:guid}")]
    public async Task<IActionResult> UpdateBox(Guid boxId, [FromBody] UpdateBoxCommand command, CancellationToken cancellationToken)
    {
        if (boxId != command.BoxId)
            return BadRequest("El ID del box en la URL no coincide con el ID en el cuerpo de la solicitud.");

        await _sender.Send(command, cancellationToken);
        
        return NoContent();
    }
}