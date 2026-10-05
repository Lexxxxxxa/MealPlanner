namespace MealPlanner.Core.Models;

/// <summary>
/// Daily bounds for each nutrient (minimum and maximum)
/// </summary>
public record NutrientTargets(
    double KcalMin, double KcalMax,
    double ProteinMin, double ProteinMax,
    double FatMin, double FatMax,
    double CarbsMin, double CarbsMax)
{
    public static NutrientTargets Default2000 => new(
        KcalMin: 1900, KcalMax: 2100,
        ProteinMin: 75, ProteinMax: 150,
        FatMin: 55, FatMax: 80,
        CarbsMin: 250, CarbsMax: 330);

    public double Min(int index) => index switch
    {
        0 => KcalMin, 1 => ProteinMin, 2 => FatMin, 3 => CarbsMin,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    public double Max(int index) => index switch
    {
        0 => KcalMax, 1 => ProteinMax, 2 => FatMax, 3 => CarbsMax,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    public const int Dimensions = 4;
}
