using MedCareOS.Application.Appointments.Queries.GetDailyAgenda;
using MedCareOS.Application.Appointments.Queries.SearchAvailability;
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

            var resultado = await _sender.Send(agenda, cancellationToken);

            return Ok(resultado);
        }

        [HttpGet("disponibilidad")]
        public async Task<IActionResult> SearchAvailability([FromQuery] string especialidad, [FromQuery] DateTime desde, [FromQuery] DateTime hasta, CancellationToken cancellationToken)
        {
            var search = new SearchAvailabilityQuery(especialidad, desde, hasta);

            var resultado = await _sender.Send(search, cancellationToken);

            return Ok(resultado);
        }
    }
}
