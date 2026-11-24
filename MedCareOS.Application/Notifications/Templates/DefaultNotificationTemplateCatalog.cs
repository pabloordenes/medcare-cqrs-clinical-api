using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.Notifications.Templates;

public sealed record DefaultNotificationTemplateDefinition(
    string TemplateKey,
    NotificationChannel Channel,
    string Locale,
    int Version,
    string? SubjectTemplate,
    string BodyTemplate,
    string? MetaTemplateName,
    IReadOnlyList<string> MetaOrderedParameterKeys,
    IReadOnlyList<(string IdPrefix, string Label, bool IsUrlButton)> Buttons)
{
    public IReadOnlyList<NotificationActionButton> BuildButtons(Guid? appointmentId, string? rescheduleTokenSuffix)
    {
        if (Buttons.Count == 0)
            return [];

        var list = new List<NotificationActionButton>(Buttons.Count);
        foreach (var (idPrefix, label, isUrl) in Buttons)
        {
            if (isUrl)
            {
                list.Add(new NotificationActionButton(idPrefix, label, rescheduleTokenSuffix));
            }
            else
            {
                var payload = appointmentId.HasValue ? $"{idPrefix}:{appointmentId.Value}" : idPrefix;
                list.Add(new NotificationActionButton(payload, label));
            }
        }

        return list;
    }
}

public static class DefaultNotificationTemplateCatalog
{
    public const string DefaultLocale = "es-CL";
    public const int CurrentVersion = 1;

    public static class Keys
    {
        public const string AppointmentCreated = "appointment.created";
        public const string Reminder = "appointment.reminder";
        public const string FinalConfirmation = "appointment.final_confirmation";
        public const string OverbookNotice = "appointment.overbook_notice";
        public const string WaitingListOffer = "waiting_list.offer";
        public const string OverbookOfferCancelled = "waiting_list.overbook_offer_cancelled";
        public const string RescheduleHelp = "appointment.reschedule_help";
    }

    private static readonly IReadOnlyList<DefaultNotificationTemplateDefinition> Definitions =
    [
        // 1. T0 Informativo: cita agendada
        new(
            Keys.AppointmentCreated,
            NotificationChannel.WhatsApp,
            DefaultLocale,
            CurrentVersion,
            null,
            "Hola {{patientFirstName}}, tu cita en {{clinicName}} fue agendada para el {{appointmentDate}} a las {{appointmentTime}} ({{specialtyDisplay}}). Te enviaremos un recordatorio antes de tu hora.",
            "medcare_cita_agendada_v1",
            ["patientFirstName", "clinicName", "appointmentDate", "appointmentTime", "specialtyDisplay"],
            []),
        new(
            Keys.AppointmentCreated,
            NotificationChannel.Email,
            DefaultLocale,
            CurrentVersion,
            "Cita agendada en {{clinicName}} - {{appointmentDate}} {{appointmentTime}}",
            "Hola {{patientFirstName}},\n\nTu cita en {{clinicName}} fue agendada para el {{appointmentDate}} a las {{appointmentTime}} ({{specialtyDisplay}}).\nTe enviaremos un recordatorio antes de tu hora.\n\nAtentamente,\n{{clinicName}}",
            null,
            [],
            []),

        // 2. Recordatorio (con botones Confirmar/Cancelar)
        new(
            Keys.Reminder,
            NotificationChannel.WhatsApp,
            DefaultLocale,
            CurrentVersion,
            null,
            "Hola {{patientFirstName}}, te recordamos tu cita en {{clinicName}} el {{appointmentDate}} a las {{appointmentTime}} ({{specialtyDisplay}}). Por favor confirma o cancela tu asistencia.",
            "medcare_recordatorio_cita_v1",
            ["patientFirstName", "clinicName", "appointmentDate", "appointmentTime", "specialtyDisplay"],
            [("CONFIRM", "Confirmar", false), ("CANCEL", "Cancelar", false)]),
        new(
            Keys.Reminder,
            NotificationChannel.Email,
            DefaultLocale,
            CurrentVersion,
            "Recordatorio de cita en {{clinicName}} - {{appointmentDate}} {{appointmentTime}}",
            "Hola {{patientFirstName}},\n\nTe recordamos tu cita en {{clinicName}} para el {{appointmentDate}} a las {{appointmentTime}} ({{specialtyDisplay}}).\n\nConfirmar asistencia: {{confirmUrl}}\nCancelar cita: {{cancelUrl}}\n\nAtentamente,\n{{clinicName}}",
            null,
            [],
            [("CONFIRM", "Confirmar", false), ("CANCEL", "Cancelar", false)]),

        // 3. Confirmación final
        new(
            Keys.FinalConfirmation,
            NotificationChannel.WhatsApp,
            DefaultLocale,
            CurrentVersion,
            null,
            "Hola {{patientFirstName}}, confirma tu hora del {{appointmentDate}} a las {{appointmentTime}} en {{clinicName}} antes de {{deadline}}. Si no confirmas, podríamos asignar tu horario a otro paciente (sobrecupo) y tendrás que reagendar por tu cuenta. Si ambos pacientes llegan, se atenderá primero a quien haya confirmado antes.",
            "medcare_confirmacion_final_v1",
            ["patientFirstName", "appointmentDate", "appointmentTime", "clinicName", "deadline"],
            [("CONFIRM", "Confirmar", false), ("CANCEL", "Cancelar", false)]),
        new(
            Keys.FinalConfirmation,
            NotificationChannel.Email,
            DefaultLocale,
            CurrentVersion,
            "Última confirmación de tu cita en {{clinicName}} - {{appointmentDate}} {{appointmentTime}}",
            "Hola {{patientFirstName}},\n\nConfirma tu hora del {{appointmentDate}} a las {{appointmentTime}} en {{clinicName}} antes de {{deadline}}. Si no confirmas, podríamos asignar tu horario a otro paciente (sobrecupo) y tendrás que reagendar por tu cuenta. Si ambos pacientes llegan, se atenderá primero a quien haya confirmado antes.\n\nConfirmar asistencia: {{confirmUrl}}\nCancelar cita: {{cancelUrl}}\n\nAtentamente,\n{{clinicName}}",
            null,
            [],
            [("CONFIRM", "Confirmar", false), ("CANCEL", "Cancelar", false)]),

        // 4. Aviso de sobrecupo
        new(
            Keys.OverbookNotice,
            NotificationChannel.WhatsApp,
            DefaultLocale,
            CurrentVersion,
            null,
            "Hola {{patientFirstName}}, tu hora del {{appointmentDate}} a las {{appointmentTime}} en {{clinicName}} pasó a modalidad sobrecupo por falta de confirmación previa. Si aún asistirás, confirma ahora; si ambos pacientes llegan, se atenderá primero a quien haya confirmado antes.",
            "medcare_aviso_sobrecupo_v1",
            ["patientFirstName", "appointmentDate", "appointmentTime", "clinicName"],
            [("CONFIRM", "Confirmar", false), ("CANCEL", "Cancelar", false)]),
        new(
            Keys.OverbookNotice,
            NotificationChannel.Email,
            DefaultLocale,
            CurrentVersion,
            "Aviso importante sobre tu cita en {{clinicName}} - {{appointmentDate}} {{appointmentTime}}",
            "Hola {{patientFirstName}},\n\nTu hora del {{appointmentDate}} a las {{appointmentTime}} en {{clinicName}} pasó a modalidad sobrecupo por falta de confirmación previa. Si aún asistirás, confirma ahora; si ambos pacientes llegan, se atenderá primero a quien haya confirmado antes.\n\nConfirmar asistencia: {{confirmUrl}}\nCancelar cita: {{cancelUrl}}\n\nAtentamente,\n{{clinicName}}",
            null,
            [],
            [("CONFIRM", "Confirmar", false), ("CANCEL", "Cancelar", false)]),

        // 5. Oferta de lista de espera
        new(
            Keys.WaitingListOffer,
            NotificationChannel.WhatsApp,
            DefaultLocale,
            CurrentVersion,
            null,
            "Hola {{patientFirstName}}, se habilitó un cupo en {{clinicName}} ({{specialtyDisplay}}) para el {{appointmentDate}} a las {{appointmentTime}}. Confirma antes de {{deadline}} si deseas tomar este horario.",
            "medcare_cupo_lista_espera_v1",
            ["patientFirstName", "clinicName", "specialtyDisplay", "appointmentDate", "appointmentTime", "deadline"],
            [("CONFIRM", "Tomar cupo", false), ("CANCEL", "Rechazar", false)]),
        new(
            Keys.WaitingListOffer,
            NotificationChannel.Email,
            DefaultLocale,
            CurrentVersion,
            "Cupo disponible en {{clinicName}} - {{appointmentDate}} {{appointmentTime}}",
            "Hola {{patientFirstName}},\n\nSe habilitó un cupo en {{clinicName}} ({{specialtyDisplay}}) para el {{appointmentDate}} a las {{appointmentTime}}. Confirma antes de {{deadline}} si deseas tomar este horario.\n\nTomar cupo: {{confirmUrl}}\nRechazar oferta: {{cancelUrl}}\n\nAtentamente,\n{{clinicName}}",
            null,
            [],
            [("CONFIRM", "Tomar cupo", false), ("CANCEL", "Rechazar", false)]),

        // 6. OverbookOfferCancelled: disculpa al candidato de lista de espera cuando el paciente original confirma tarde
        new(
            Keys.OverbookOfferCancelled,
            NotificationChannel.WhatsApp,
            DefaultLocale,
            CurrentVersion,
            null,
            "Hola {{patientFirstName}}, lamentamos informarte que el cupo disponible para el {{appointmentDate}} a las {{appointmentTime}} en {{clinicName}} se desactivó porque el paciente original confirmó su asistencia. Mantienes tu prioridad en nuestra lista de espera.",
            "medcare_cupo_cancelado_v1",
            ["patientFirstName", "appointmentDate", "appointmentTime", "clinicName"],
            []),
        new(
            Keys.OverbookOfferCancelled,
            NotificationChannel.Email,
            DefaultLocale,
            CurrentVersion,
            "Actualización sobre cupo ofrecido en {{clinicName}} - {{appointmentDate}}",
            "Hola {{patientFirstName}},\n\nLamentamos informarte que el cupo disponible para el {{appointmentDate}} a las {{appointmentTime}} en {{clinicName}} se desactivó porque el paciente original confirmó su asistencia. Mantienes tu prioridad en nuestra lista de espera.\n\nAtentamente,\n{{clinicName}}",
            null,
            [],
            []),

        // 7. RescheduleHelp: enlace al autoservicio para el paciente desplazado por sobrecupo
        new(
            Keys.RescheduleHelp,
            NotificationChannel.WhatsApp,
            DefaultLocale,
            CurrentVersion,
            null,
            "Hola {{patientFirstName}}, tu horario del {{appointmentDate}} a las {{appointmentTime}} en {{clinicName}} fue asignado a otro paciente por sobrecupo. Puedes elegir una nueva hora directamente desde el enlace adjunto.",
            "medcare_ayuda_reagendamiento_v1",
            ["patientFirstName", "appointmentDate", "appointmentTime", "clinicName"],
            [("RESCHEDULE", "Reagendar hora", true)]),
        new(
            Keys.RescheduleHelp,
            NotificationChannel.Email,
            DefaultLocale,
            CurrentVersion,
            "Reagenda tu hora en {{clinicName}}",
            "Hola {{patientFirstName}},\n\nTu horario del {{appointmentDate}} a las {{appointmentTime}} en {{clinicName}} fue asignado a otro paciente por sobrecupo. Puedes elegir una nueva hora directamente desde el siguiente enlace de autoservicio:\n\nReagendar hora: {{rescheduleUrl}}\n\nAtentamente,\n{{clinicName}}",
            null,
            [],
            [("RESCHEDULE", "Reagendar hora", true)])
    ];

    public static IReadOnlyCollection<DefaultNotificationTemplateDefinition> All => Definitions;

    public static DefaultNotificationTemplateDefinition Resolve(
        string templateKey,
        NotificationChannel channel,
        string? locale = null)
    {
        var effectiveLocale = string.IsNullOrWhiteSpace(locale) ? DefaultLocale : locale.Trim();

        var match = Definitions.FirstOrDefault(d =>
            string.Equals(d.TemplateKey, templateKey, StringComparison.OrdinalIgnoreCase) &&
            d.Channel == channel &&
            string.Equals(d.Locale, effectiveLocale, StringComparison.OrdinalIgnoreCase));

        match ??= Definitions.FirstOrDefault(d =>
            string.Equals(d.TemplateKey, templateKey, StringComparison.OrdinalIgnoreCase) &&
            d.Channel == channel &&
            string.Equals(d.Locale, DefaultLocale, StringComparison.OrdinalIgnoreCase));

        return match ?? throw new KeyNotFoundException(
            $"No existe plantilla predeterminada para '{templateKey}' ({channel}, {effectiveLocale}).");
    }

    public static string GetTemplateKey(NotificationType type) => type switch
    {
        NotificationType.AppointmentCreated => Keys.AppointmentCreated,
        NotificationType.Reminder => Keys.Reminder,
        NotificationType.FinalConfirmation => Keys.FinalConfirmation,
        NotificationType.OverbookNotice => Keys.OverbookNotice,
        NotificationType.WaitingListOffer => Keys.WaitingListOffer,
        NotificationType.OverbookOfferCancelled => Keys.OverbookOfferCancelled,
        NotificationType.RescheduleHelp => Keys.RescheduleHelp,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Tipo de notificación no soportado.")
    };
}
