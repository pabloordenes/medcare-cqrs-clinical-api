using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace MedCareOS.Application.Appointments.Queries.GetDailyAgenda
{
    public record GetDailyAgendaQuery(
        Guid MedicoId,
        DateTime Fecha) : IRequest<List<AgendaBloqueDto>>;
}
