using MedCareOS.Application.Appointments.Queries.GetDailyAgenda;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace MedCareOS.Application.Appointments.Queries.SearchAvailability
{
    public record SearchAvailabilityQuery(
    string Especialidad,
    DateTime Desde,
    DateTime Hasta
        ) : IRequest<List<AgendaBloqueDto>>
    {
    }
}
