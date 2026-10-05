namespace MealPlanner.Core.Models;

/// <summary>
/// Food product: nutritional value and price per 100 g
/// </summary>
public class Food
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    public double KcalPer100 { get; set; }
    public double ProteinPer100 { get; set; }
    public double FatPer100 { get; set; }
    public double CarbsPer100 { get; set; }

    public double PricePer100 { get; set; }

    public double MaxGrams { get; set; } = 400;

    public Nutrients NutrientsFor(double grams) =>
        new Nutrients(KcalPer100, ProteinPer100, FatPer100, CarbsPer100) * (grams / 100.0);

    public double CostFor(double grams) => PricePer100 * grams / 100.0;
}
