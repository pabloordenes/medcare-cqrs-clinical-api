using MedCareOS.Application.Patients.Commands.CreatePatient;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    private readonly ISender _sender;
    
    public PatientsController(ISender sender)
    {
        _sender = sender;
    }
    
    [HttpPost]
    public async Task<IActionResult> CreatePatient([FromBody] CreatePatientCommand command, CancellationToken cancellationToken)
    {
        var patientId = await _sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new { id = patientId });
    }
}