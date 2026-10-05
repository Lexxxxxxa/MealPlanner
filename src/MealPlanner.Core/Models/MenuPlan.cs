namespace MealPlanner.Core.Models;

/// <summary>
/// Result of the daily menu selection algorithm
/// </summary>
public class MenuPlan
{
    public string Algorithm { get; init; } = string.Empty;
    public IReadOnlyList<MenuItem> Items { get; init; } = Array.Empty<MenuItem>();
    public bool IsFeasible { get; init; }
    public double ElapsedMs { get; init; }
    public string? Note { get; init; }

    public Nutrients Totals => Items.Aggregate(Nutrients.Zero, (acc, i) => acc + i.Nutrients);
    public double TotalCost => Items.Sum(i => i.Cost);

    public double Deviation(NutrientTargets t)
    {
        var totals = Totals;
        double dev = 0;
        for (int k = 0; k < NutrientTargets.Dimensions; k++)
        {
            double v = totals[k];
            if (v < t.Min(k)) dev += (t.Min(k) - v) / t.Min(k);
            else if (v > t.Max(k)) dev += (v - t.Max(k)) / t.Max(k);
        }
        return dev;
    }
}
