using System.Text.Json;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.ValueObjects;

namespace MedCareOS.Domain.Entities;

public class TenantConfirmationPolicy
{
    public const int MaxTimeZoneIdLength = 64;
    public const string DefaultExcludedClinicalSpecialtiesJson = "[\"oncologia\",\"preoperatorio\",\"cirugia\"]";
    public const string DefaultExcludedClinicalKeywordsJson = "[\"pre-operatorio\",\"preoperatorio\",\"cirugia\",\"embarazo alto riesgo\"]";

    public Guid TenantId { get; private set; }

    // Offsets temporales relativos al inicio de la cita (en horas) y salvaguardas
    public int Reminder1OffsetHours { get; private set; } = 72;
    public int Reminder2OffsetHours { get; private set; } = 48;
    public int FinalConfirmationOffsetHours { get; private set; } = 24;
    public int DeadlineOffsetHours { get; private set; } = 12;
    public int MinimumDelayAfterCreationMinutes { get; private set; } = 30;
    public int SameDayReminderMinLeadHours { get; private set; } = 3;

    // Ventana horaria local del tenant
    public string TimeZoneId { get; private set; } = "America/Santiago";
    public TimeOnly? QuietHoursStartLocal { get; private set; }
    public TimeOnly? QuietHoursEndLocal { get; private set; }

    // Estrategia de canales y re-score
    public PrimaryChannelStrategy PrimaryChannelStrategy { get; private set; } = PrimaryChannelStrategy.PatientPreferred;
    public AlternateChannelStrategy AlternateChannelStrategy { get; private set; } = AlternateChannelStrategy.AlternateIfAvailable;
    public bool EnableRescoreAtReminder2 { get; private set; } = true;
    public bool CountSentAsDeliveredForUnanswered { get; private set; }

    // Pesos en espacio logit para señales dinámicas de riesgo
    public decimal WeightConfirmedEarly { get; private set; } = -1.5000m;
    public decimal WeightReminderUnanswered { get; private set; } = 0.4000m;
    public decimal MaxReminderUnansweredCumulative { get; private set; } = 1.2000m;
    public decimal WeightDeliveryFailed { get; private set; } = 0.8000m;
    public decimal WeightRepliedWithoutConfirmation { get; private set; } = 0.0000m;
    public decimal WeightFinalConfirmed { get; private set; } = 0.0000m;

    // Umbrales de histéresis para cambio de banda efectivo
    public decimal MediumExitThreshold { get; private set; } = 0.4000m;
    public decimal MediumEnterThreshold { get; private set; } = 0.5000m;
    public decimal HighExitThreshold { get; private set; } = 0.5500m;
    public decimal HighEnterThreshold { get; private set; } = 0.7000m;

    // Política de sobrecupo, exclusiones clínicas, backfill y autonomía (E4)
    public bool AutoOverbookEnabled { get; private set; }
    public decimal AutoOverbookMinEffectiveScore { get; private set; } = 0.8500m;
    public decimal HitlOverbookMinEffectiveScore { get; private set; } = 0.5000m;
    public int ExcludedSeniorAgeYears { get; private set; } = 70;
    public string ExcludedClinicalSpecialtiesJson { get; private set; } = DefaultExcludedClinicalSpecialtiesJson;
    public string ExcludedClinicalKeywordsJson { get; private set; } = DefaultExcludedClinicalKeywordsJson;
    public int WaitingListOfferTtlMinutes { get; private set; } = 60;
    public int MaxWaitingListCascadeOffers { get; private set; } = 5;
    public int MinLeadMinutesForWaitingListOffer { get; private set; } = 60;
    public int MaxReschedulesPerAppointment { get; private set; } = 3;
    public int MinShadowEvaluationsForActivation { get; private set; } = 200;
    public decimal MaxFalsePositiveRateForActivation { get; private set; } = 0.1500m;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TenantConfirmationPolicy() { } // ef

    public TimeSpan Reminder1Offset => TimeSpan.FromHours(Reminder1OffsetHours);
    public TimeSpan Reminder2Offset => TimeSpan.FromHours(Reminder2OffsetHours);
    public TimeSpan FinalConfirmationOffset => TimeSpan.FromHours(FinalConfirmationOffsetHours);
    public TimeSpan DeadlineOffset => TimeSpan.FromHours(DeadlineOffsetHours);
    public TimeSpan MinimumDelayAfterCreation => TimeSpan.FromMinutes(MinimumDelayAfterCreationMinutes);
    public TimeSpan SameDayReminderMinLead => TimeSpan.FromHours(SameDayReminderMinLeadHours);
    public TimeSpan WaitingListOfferTtl => TimeSpan.FromMinutes(WaitingListOfferTtlMinutes);

    public IReadOnlyList<string> GetExcludedClinicalSpecialties() => ParseStringListJson(ExcludedClinicalSpecialtiesJson);

    public IReadOnlyList<string> GetExcludedClinicalKeywords() => ParseStringListJson(ExcludedClinicalKeywordsJson);

    public HysteresisThresholds GetHysteresisThresholds() =>
        HysteresisThresholds.Create(
            MediumExitThreshold,
            MediumEnterThreshold,
            HighExitThreshold,
            HighEnterThreshold);

    public static TenantConfirmationPolicy CreateDefault(Guid tenantId, DateTimeOffset? utcNow = null)
    {
        return Create(
            tenantId: tenantId,
            reminder1OffsetHours: 72,
            reminder2OffsetHours: 48,
            finalConfirmationOffsetHours: 24,
            deadlineOffsetHours: 12,
            minimumDelayAfterCreationMinutes: 30,
            sameDayReminderMinLeadHours: 3,
            timeZoneId: "America/Santiago",
            quietHoursStartLocal: null,
            quietHoursEndLocal: null,
            primaryChannelStrategy: PrimaryChannelStrategy.PatientPreferred,
            alternateChannelStrategy: AlternateChannelStrategy.AlternateIfAvailable,
            enableRescoreAtReminder2: true,
            countSentAsDeliveredForUnanswered: false,
            weightConfirmedEarly: -1.5000m,
            weightReminderUnanswered: 0.4000m,
            maxReminderUnansweredCumulative: 1.2000m,
            weightDeliveryFailed: 0.8000m,
            weightRepliedWithoutConfirmation: 0.0000m,
            weightFinalConfirmed: 0.0000m,
            mediumExitThreshold: 0.4000m,
            mediumEnterThreshold: 0.5000m,
            highExitThreshold: 0.5500m,
            highEnterThreshold: 0.7000m,
            utcNow: utcNow);
    }

    public static TenantConfirmationPolicy Create(
        Guid tenantId,
        int reminder1OffsetHours,
        int reminder2OffsetHours,
        int finalConfirmationOffsetHours,
        int deadlineOffsetHours,
        int minimumDelayAfterCreationMinutes,
        int sameDayReminderMinLeadHours,
        string timeZoneId,
        TimeOnly? quietHoursStartLocal,
        TimeOnly? quietHoursEndLocal,
        PrimaryChannelStrategy primaryChannelStrategy,
        AlternateChannelStrategy alternateChannelStrategy,
        bool enableRescoreAtReminder2,
        bool countSentAsDeliveredForUnanswered,
        decimal weightConfirmedEarly,
        decimal weightReminderUnanswered,
        decimal maxReminderUnansweredCumulative,
        decimal weightDeliveryFailed,
        decimal weightRepliedWithoutConfirmation,
        decimal weightFinalConfirmed,
        decimal mediumExitThreshold,
        decimal mediumEnterThreshold,
        decimal highExitThreshold,
        decimal highEnterThreshold,
        DateTimeOffset? utcNow = null)
    {
        ValidateInvariants(
            tenantId,
            reminder1OffsetHours,
            reminder2OffsetHours,
            finalConfirmationOffsetHours,
            deadlineOffsetHours,
            minimumDelayAfterCreationMinutes,
            sameDayReminderMinLeadHours,
            timeZoneId,
            quietHoursStartLocal,
            quietHoursEndLocal,
            weightConfirmedEarly,
            weightReminderUnanswered,
            maxReminderUnansweredCumulative,
            weightDeliveryFailed,
            mediumExitThreshold,
            mediumEnterThreshold,
            highExitThreshold,
            highEnterThreshold);

        var now = utcNow ?? DateTimeOffset.UtcNow;

        return new TenantConfirmationPolicy
        {
            TenantId = tenantId,
            Reminder1OffsetHours = reminder1OffsetHours,
            Reminder2OffsetHours = reminder2OffsetHours,
            FinalConfirmationOffsetHours = finalConfirmationOffsetHours,
            DeadlineOffsetHours = deadlineOffsetHours,
            MinimumDelayAfterCreationMinutes = minimumDelayAfterCreationMinutes,
            SameDayReminderMinLeadHours = sameDayReminderMinLeadHours,
            TimeZoneId = timeZoneId.Trim(),
            QuietHoursStartLocal = quietHoursStartLocal,
            QuietHoursEndLocal = quietHoursEndLocal,
            PrimaryChannelStrategy = primaryChannelStrategy,
            AlternateChannelStrategy = alternateChannelStrategy,
            EnableRescoreAtReminder2 = enableRescoreAtReminder2,
            CountSentAsDeliveredForUnanswered = countSentAsDeliveredForUnanswered,
            WeightConfirmedEarly = weightConfirmedEarly,
            WeightReminderUnanswered = weightReminderUnanswered,
            MaxReminderUnansweredCumulative = maxReminderUnansweredCumulative,
            WeightDeliveryFailed = weightDeliveryFailed,
            WeightRepliedWithoutConfirmation = weightRepliedWithoutConfirmation,
            WeightFinalConfirmed = weightFinalConfirmed,
            MediumExitThreshold = mediumExitThreshold,
            MediumEnterThreshold = mediumEnterThreshold,
            HighExitThreshold = highExitThreshold,
            HighEnterThreshold = highEnterThreshold,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(
        int reminder1OffsetHours,
        int reminder2OffsetHours,
        int finalConfirmationOffsetHours,
        int deadlineOffsetHours,
        int minimumDelayAfterCreationMinutes,
        int sameDayReminderMinLeadHours,
        string timeZoneId,
        TimeOnly? quietHoursStartLocal,
        TimeOnly? quietHoursEndLocal,
        PrimaryChannelStrategy primaryChannelStrategy,
        AlternateChannelStrategy alternateChannelStrategy,
        bool enableRescoreAtReminder2,
        bool countSentAsDeliveredForUnanswered,
        decimal weightConfirmedEarly,
        decimal weightReminderUnanswered,
        decimal maxReminderUnansweredCumulative,
        decimal weightDeliveryFailed,
        decimal weightRepliedWithoutConfirmation,
        decimal weightFinalConfirmed,
        decimal mediumExitThreshold,
        decimal mediumEnterThreshold,
        decimal highExitThreshold,
        decimal highEnterThreshold,
        DateTimeOffset? utcNow = null)
    {
        ValidateInvariants(
            TenantId,
            reminder1OffsetHours,
            reminder2OffsetHours,
            finalConfirmationOffsetHours,
            deadlineOffsetHours,
            minimumDelayAfterCreationMinutes,
            sameDayReminderMinLeadHours,
            timeZoneId,
            quietHoursStartLocal,
            quietHoursEndLocal,
            weightConfirmedEarly,
            weightReminderUnanswered,
            maxReminderUnansweredCumulative,
            weightDeliveryFailed,
            mediumExitThreshold,
            mediumEnterThreshold,
            highExitThreshold,
            highEnterThreshold);

        Reminder1OffsetHours = reminder1OffsetHours;
        Reminder2OffsetHours = reminder2OffsetHours;
        FinalConfirmationOffsetHours = finalConfirmationOffsetHours;
        DeadlineOffsetHours = deadlineOffsetHours;
        MinimumDelayAfterCreationMinutes = minimumDelayAfterCreationMinutes;
        SameDayReminderMinLeadHours = sameDayReminderMinLeadHours;
        TimeZoneId = timeZoneId.Trim();
        QuietHoursStartLocal = quietHoursStartLocal;
        QuietHoursEndLocal = quietHoursEndLocal;
        PrimaryChannelStrategy = primaryChannelStrategy;
        AlternateChannelStrategy = alternateChannelStrategy;
        EnableRescoreAtReminder2 = enableRescoreAtReminder2;
        CountSentAsDeliveredForUnanswered = countSentAsDeliveredForUnanswered;
        WeightConfirmedEarly = weightConfirmedEarly;
        WeightReminderUnanswered = weightReminderUnanswered;
        MaxReminderUnansweredCumulative = maxReminderUnansweredCumulative;
        WeightDeliveryFailed = weightDeliveryFailed;
        WeightRepliedWithoutConfirmation = weightRepliedWithoutConfirmation;
        WeightFinalConfirmed = weightFinalConfirmed;
        MediumExitThreshold = mediumExitThreshold;
        MediumEnterThreshold = mediumEnterThreshold;
        HighExitThreshold = highExitThreshold;
        HighEnterThreshold = highEnterThreshold;
        UpdatedAt = utcNow ?? DateTimeOffset.UtcNow;
    }

    public void ConfigureOverbookPolicy(
        bool autoOverbookEnabled,
        decimal autoOverbookMinEffectiveScore,
        decimal hitlOverbookMinEffectiveScore,
        int excludedSeniorAgeYears,
        IEnumerable<string> excludedClinicalSpecialties,
        IEnumerable<string> excludedClinicalKeywords,
        int waitingListOfferTtlMinutes,
        int maxWaitingListCascadeOffers,
        int minLeadMinutesForWaitingListOffer,
        int maxReschedulesPerAppointment,
        int minShadowEvaluationsForActivation,
        decimal maxFalsePositiveRateForActivation,
        DateTimeOffset? utcNow = null)
    {
        if (hitlOverbookMinEffectiveScore is <= 0m or >= 1m)
            throw new ArgumentOutOfRangeException(nameof(hitlOverbookMinEffectiveScore), "HitlOverbookMinEffectiveScore debe estar en (0, 1).");

        if (autoOverbookMinEffectiveScore <= hitlOverbookMinEffectiveScore || autoOverbookMinEffectiveScore >= 1m)
            throw new ArgumentOutOfRangeException(nameof(autoOverbookMinEffectiveScore), "AutoOverbookMinEffectiveScore debe ser mayor a HitlOverbookMinEffectiveScore y menor a 1.");

        if (excludedSeniorAgeYears is < 18 or > 120)
            throw new ArgumentOutOfRangeException(nameof(excludedSeniorAgeYears), "ExcludedSeniorAgeYears debe estar entre 18 y 120.");

        if (waitingListOfferTtlMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(waitingListOfferTtlMinutes), "WaitingListOfferTtlMinutes debe ser mayor a 0.");

        if (maxWaitingListCascadeOffers < 1)
            throw new ArgumentOutOfRangeException(nameof(maxWaitingListCascadeOffers), "MaxWaitingListCascadeOffers debe ser al menos 1.");

        if (minLeadMinutesForWaitingListOffer < 0)
            throw new ArgumentOutOfRangeException(nameof(minLeadMinutesForWaitingListOffer), "MinLeadMinutesForWaitingListOffer no puede ser negativo.");

        if (maxReschedulesPerAppointment < 0)
            throw new ArgumentOutOfRangeException(nameof(maxReschedulesPerAppointment), "MaxReschedulesPerAppointment no puede ser negativo.");

        if (minShadowEvaluationsForActivation < 1)
            throw new ArgumentOutOfRangeException(nameof(minShadowEvaluationsForActivation), "MinShadowEvaluationsForActivation debe ser al menos 1.");

        if (maxFalsePositiveRateForActivation is <= 0m or >= 1m)
            throw new ArgumentOutOfRangeException(nameof(maxFalsePositiveRateForActivation), "MaxFalsePositiveRateForActivation debe estar en (0, 1).");

        var normalizedSpecialties = NormalizeStringList(excludedClinicalSpecialties);
        var normalizedKeywords = NormalizeStringList(excludedClinicalKeywords);

        AutoOverbookEnabled = autoOverbookEnabled;
        AutoOverbookMinEffectiveScore = Math.Round(autoOverbookMinEffectiveScore, 4, MidpointRounding.AwayFromZero);
        HitlOverbookMinEffectiveScore = Math.Round(hitlOverbookMinEffectiveScore, 4, MidpointRounding.AwayFromZero);
        ExcludedSeniorAgeYears = excludedSeniorAgeYears;
        ExcludedClinicalSpecialtiesJson = JsonSerializer.Serialize(normalizedSpecialties);
        ExcludedClinicalKeywordsJson = JsonSerializer.Serialize(normalizedKeywords);
        WaitingListOfferTtlMinutes = waitingListOfferTtlMinutes;
        MaxWaitingListCascadeOffers = maxWaitingListCascadeOffers;
        MinLeadMinutesForWaitingListOffer = minLeadMinutesForWaitingListOffer;
        MaxReschedulesPerAppointment = maxReschedulesPerAppointment;
        MinShadowEvaluationsForActivation = minShadowEvaluationsForActivation;
        MaxFalsePositiveRateForActivation = Math.Round(maxFalsePositiveRateForActivation, 4, MidpointRounding.AwayFromZero);
        UpdatedAt = utcNow ?? DateTimeOffset.UtcNow;
    }

    public void SetAutoOverbookEnabled(bool enabled, DateTimeOffset? utcNow = null)
    {
        AutoOverbookEnabled = enabled;
        UpdatedAt = utcNow ?? DateTimeOffset.UtcNow;
    }

    private static IReadOnlyList<string> ParseStringListJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            return parsed is null ? [] : NormalizeStringList(parsed);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<string> NormalizeStringList(IEnumerable<string>? values)
    {
        if (values is null)
            return [];

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ValidateInvariants(
        Guid tenantId,
        int reminder1OffsetHours,
        int reminder2OffsetHours,
        int finalConfirmationOffsetHours,
        int deadlineOffsetHours,
        int minimumDelayAfterCreationMinutes,
        int sameDayReminderMinLeadHours,
        string timeZoneId,
        TimeOnly? quietHoursStartLocal,
        TimeOnly? quietHoursEndLocal,
        decimal weightConfirmedEarly,
        decimal weightReminderUnanswered,
        decimal maxReminderUnansweredCumulative,
        decimal weightDeliveryFailed,
        decimal mediumExitThreshold,
        decimal mediumEnterThreshold,
        decimal highExitThreshold,
        decimal highEnterThreshold)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId no puede estar vacío.", nameof(tenantId));

        if (deadlineOffsetHours <= 0 ||
            finalConfirmationOffsetHours <= deadlineOffsetHours ||
            reminder2OffsetHours <= finalConfirmationOffsetHours ||
            reminder1OffsetHours <= reminder2OffsetHours)
        {
            throw new ArgumentException(
                "Los offsets deben cumplir estrictamente: Reminder1 > Reminder2 > FinalConfirmation > Deadline > 0.");
        }

        if (minimumDelayAfterCreationMinutes is < 0 or > 360)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumDelayAfterCreationMinutes),
                minimumDelayAfterCreationMinutes,
                "MinimumDelayAfterCreationMinutes debe estar entre 0 y 360.");
        }

        if (sameDayReminderMinLeadHours < 0 || sameDayReminderMinLeadHours > deadlineOffsetHours)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sameDayReminderMinLeadHours),
                sameDayReminderMinLeadHours,
                "SameDayReminderMinLeadHours debe estar entre 0 y DeadlineOffsetHours.");
        }

        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Trim().Length > MaxTimeZoneIdLength)
        {
            throw new ArgumentException(
                $"TimeZoneId es obligatorio y no puede exceder {MaxTimeZoneIdLength} caracteres.",
                nameof(timeZoneId));
        }

        if (quietHoursStartLocal.HasValue != quietHoursEndLocal.HasValue)
        {
            throw new ArgumentException(
                "QuietHoursStartLocal y QuietHoursEndLocal deben especificarse juntos o quedar ambos nulos.");
        }

        if (quietHoursStartLocal.HasValue && quietHoursStartLocal.Value == quietHoursEndLocal!.Value)
        {
            throw new ArgumentException(
                "QuietHoursStartLocal y QuietHoursEndLocal no pueden ser iguales.");
        }

        if (weightConfirmedEarly > 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weightConfirmedEarly),
                weightConfirmedEarly,
                "WeightConfirmedEarly no puede ser positivo.");
        }

        if (weightReminderUnanswered < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weightReminderUnanswered),
                weightReminderUnanswered,
                "WeightReminderUnanswered no puede ser negativo.");
        }

        if (maxReminderUnansweredCumulative < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxReminderUnansweredCumulative),
                maxReminderUnansweredCumulative,
                "MaxReminderUnansweredCumulative no puede ser negativo.");
        }

        if (weightDeliveryFailed < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weightDeliveryFailed),
                weightDeliveryFailed,
                "WeightDeliveryFailed no puede ser negativo.");
        }

        HysteresisThresholds.Create(
            mediumExitThreshold,
            mediumEnterThreshold,
            highExitThreshold,
            highEnterThreshold);
    }
}
