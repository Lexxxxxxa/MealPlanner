using MealPlanner.Core.Models;

namespace MealPlanner.Core.Recommendation;

/// <summary>
/// Baseline: random dish order (deterministic by seed)
/// </summary>
public class RandomRecommender : IDishRecommender
{
    private readonly int _seed;
    public RandomRecommender(int seed = 1) => _seed = seed;

    public string Name => "Random";

    public IReadOnlyList<Recommendation> Recommend(
        IReadOnlyList<Dish> dishes, IReadOnlyDictionary<int, double> pantry,
        RecommendationContext context, int top)
    {
        var rnd = new Random(_seed);
        return dishes
            .Select(d => new Recommendation(d, rnd.NextDouble(), CoverageInfo.Analyze(d, pantry)))
            .OrderByDescending(r => r.Score)
            .Take(top)
            .ToList();
    }
}

/// <summary>
/// Share of ingredients present in stock at all (ignoring quantity)
/// </summary>
public class BinaryCoverageRecommender : IDishRecommender
{
    public string Name => "BinaryCoverage";

    public IReadOnlyList<Recommendation> Recommend(
        IReadOnlyList<Dish> dishes, IReadOnlyDictionary<int, double> pantry,
        RecommendationContext context, int top) =>
        dishes
            .Select(d => { var c = CoverageInfo.Analyze(d, pantry); return new Recommendation(d, c.IngredientShare, c); })
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Dish.Name, StringComparer.Ordinal)
            .Take(top)
            .ToList();
}

/// <summary>
/// Share of required ingredient mass covered by stock
/// </summary>
public class WeightedCoverageRecommender : IDishRecommender
{
    public string Name => "WeightedCoverage";

    public IReadOnlyList<Recommendation> Recommend(
        IReadOnlyList<Dish> dishes, IReadOnlyDictionary<int, double> pantry,
        RecommendationContext context, int top) =>
        dishes
            .Select(d => { var c = CoverageInfo.Analyze(d, pantry); return new Recommendation(d, c.GramShare, c); })
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Coverage.MissingCost)
            .Take(top)
            .ToList();
}

/// <summary>
/// Proposed approach: coverage with IDF weights (rare ingredients are harder to buy or replace),
/// shopping cost penalty and matching of portion calories with the target
/// score = coverage_idf * fit_kcal - lambda * missingCost
/// </summary>
public class SmartRecommender : IDishRecommender
{
    public string Name => "Smart";

    public double CostPenalty { get; init; } = 0.05;

    public double CalorieWeight { get; init; } = 0.5;

    public IReadOnlyList<Recommendation> Recommend(
        IReadOnlyList<Dish> dishes, IReadOnlyDictionary<int, double> pantry,
        RecommendationContext context, int top)
    {
        var idf = ComputeIdf(dishes);

        return dishes
            .Select(d =>
            {
                var c = CoverageInfo.Analyze(d, pantry);
                double num = 0, den = 0;
                foreach (var ing in d.Ingredients)
                {
                    double w = idf.GetValueOrDefault(ing.Food.Id, 1.0);
                    double have = pantry.TryGetValue(ing.Food.Id, out var g) ? g : 0;
                    num += w * Math.Min(1.0, have / ing.Grams);
                    den += w;
                }
                double cov = den > 0 ? num / den : 0;

                double fit = 1.0;
                if (context.TargetKcalPerServing is { } target && target > 0)
                {
                    double dev = Math.Min(1.0, Math.Abs(d.PerServing.Kcal - target) / target);
                    fit = 1.0 - CalorieWeight * dev;
                }

                double score = cov * fit - CostPenalty * c.MissingCost;
                return new Recommendation(d, score, c);
            })
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Dish.Name, StringComparer.Ordinal)
            .Take(top)
            .ToList();
    }

    private static Dictionary<int, double> ComputeIdf(IReadOnlyList<Dish> dishes)
    {
        var df = new Dictionary<int, int>();
        foreach (var d in dishes)
            foreach (var id in d.Ingredients.Select(i => i.Food.Id).Distinct())
                df[id] = df.GetValueOrDefault(id) + 1;

        int n = dishes.Count;
        return df.ToDictionary(kv => kv.Key, kv => Math.Log((n + 1.0) / (kv.Value + 1.0)) + 1.0);
    }
}
