using MedCareOS.Application.Appointments.Queries.GetDailyAgenda;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MedCareOS.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AgendaController : ControllerBase
    {
        private readonly ISender _sender;

        public AgendaController(ISender sender)
        {
            _sender = sender;
        }

        [HttpGet("medico/{medicoId:guid}")]
        public async Task<IActionResult> GetDailyAgenda([FromRoute] Guid medicoId, [FromQuery] DateTime fecha, CancellationToken cancellationToken)
        {
            var agenda = new GetDailyAgendaQuery(medicoId, fecha);

            var resultado = await _sender.Send(agenda);

            return Ok(resultado);
        }
    }
}
