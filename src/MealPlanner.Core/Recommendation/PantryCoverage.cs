using MealPlanner.Core.Models;

namespace MealPlanner.Core.Recommendation;

public record MissingIngredient(Food Food, double MissingGrams)
{
    public double Cost => Food.CostFor(MissingGrams);
}

/// <summary>
/// Analysis of how well a dish is covered by available stock
/// </summary>
public record CoverageInfo(
    double IngredientShare,
    double GramShare,
    bool IsCookable,
    IReadOnlyList<MissingIngredient> Missing)
{
    public double MissingCost => Missing.Sum(m => m.Cost);

    public static CoverageInfo Analyze(Dish dish, IReadOnlyDictionary<int, double> pantry)
    {
        const double Tolerance = 1e-6;
        int present = 0;
        double covered = 0, needed = 0;
        var missing = new List<MissingIngredient>();

        foreach (var ing in dish.Ingredients)
        {
            double have = pantry.TryGetValue(ing.Food.Id, out var g) ? g : 0;
            if (have > 0) present++;
            covered += Math.Min(have, ing.Grams);
            needed += ing.Grams;
            if (have + Tolerance < ing.Grams)
                missing.Add(new MissingIngredient(ing.Food, ing.Grams - have));
        }

        int count = Math.Max(1, dish.Ingredients.Count);
        return new CoverageInfo(
            IngredientShare: (double)present / count,
            GramShare: needed > 0 ? covered / needed : 0,
            IsCookable: missing.Count == 0,
            Missing: missing);
    }
}
