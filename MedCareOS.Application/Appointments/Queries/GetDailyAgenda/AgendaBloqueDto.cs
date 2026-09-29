using System;
using System.Collections.Generic;
using System.Text;

namespace MedCareOS.Application.Appointments.Queries.GetDailyAgenda
{
    public record AgendaBloqueDto(
        Guid ScheduleBlockId,
        Guid MedicoId,
        Guid BoxId,
        DateTimeOffset FechaInicio,
        DateTimeOffset FechaFin,
        string Disponibilidad,
        Guid? CitaId,
        string PacienteNombre,
        string MotivoConsulta);
}
