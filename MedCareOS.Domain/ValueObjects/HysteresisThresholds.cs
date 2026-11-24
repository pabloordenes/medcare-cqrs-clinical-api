namespace MedCareOS.Domain.ValueObjects;

/// <summary>
/// Umbrales ordenados de histéresis para evitar flapping entre bandas de riesgo.
/// Invariante: 0 &lt; MediumExit &lt; MediumEnter &lt; HighExit &lt; HighEnter &lt; 1.
/// </summary>
public sealed record HysteresisThresholds
{
    public decimal MediumExit { get; }
    public decimal MediumEnter { get; }
    public decimal HighExit { get; }
    public decimal HighEnter { get; }

    private HysteresisThresholds(decimal mediumExit, decimal mediumEnter, decimal highExit, decimal highEnter)
    {
        MediumExit = mediumExit;
        MediumEnter = mediumEnter;
        HighExit = highExit;
        HighEnter = highEnter;
    }

    public static HysteresisThresholds Default => Create(0.40m, 0.50m, 0.55m, 0.70m);

    public static HysteresisThresholds Create(
        decimal mediumExit,
        decimal mediumEnter,
        decimal highExit,
        decimal highEnter)
    {
        if (mediumExit <= 0m || mediumExit >= 1m)
            throw new ArgumentOutOfRangeException(nameof(mediumExit), mediumExit, "MediumExit debe estar estrictamente entre 0 y 1.");

        if (mediumEnter <= mediumExit || mediumEnter >= 1m)
            throw new ArgumentOutOfRangeException(nameof(mediumEnter), mediumEnter, "MediumEnter debe ser mayor que MediumExit y menor que 1.");

        if (highExit <= mediumEnter || highExit >= 1m)
            throw new ArgumentOutOfRangeException(nameof(highExit), highExit, "HighExit debe ser mayor que MediumEnter y menor que 1.");

        if (highEnter <= highExit || highEnter >= 1m)
            throw new ArgumentOutOfRangeException(nameof(highEnter), highEnter, "HighEnter debe ser mayor que HighExit y menor que 1.");

        return new HysteresisThresholds(mediumExit, mediumEnter, highExit, highEnter);
    }
}
