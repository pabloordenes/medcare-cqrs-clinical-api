using MedCareOS.Application.Boxes.Commands.CreateBox;
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
}