using MealPlanner.Core.Models;

namespace MealPlanner.Core.Recommendation;

public enum SubstitutionMethod
{
    Random,
    Macro,
    CategoryMacro
}

public record Substitution(Food Candidate, double Distance);

/// <summary>
/// Selection of a replacement for a missing ingredient among available products
/// Distance is Euclidean over (kcal, protein, fat, carbs) per 100 g,
/// each coordinate is divided by the standard deviation over the catalog
/// </summary>
public class SubstitutionFinder
{
    private readonly double[] _scale = new double[4];
    private readonly Random _rnd;

    public SubstitutionFinder(IReadOnlyList<Food> catalogue, int seed = 1)
    {
        _rnd = new Random(seed);
        for (int k = 0; k < 4; k++)
        {
            var values = catalogue.Select(f => Per100(f, k)).ToList();
            double mean = values.Average();
            double variance = values.Sum(v => (v - mean) * (v - mean)) / Math.Max(1, values.Count);
            _scale[k] = Math.Max(1e-6, Math.Sqrt(variance));
        }
    }

    public double Distance(Food a, Food b)
    {
        double sum = 0;
        for (int k = 0; k < 4; k++)
        {
            double d = (Per100(a, k) - Per100(b, k)) / _scale[k];
            sum += d * d;
        }
        return Math.Sqrt(sum);
    }

    public IReadOnlyList<Substitution> Suggest(
        Food missing, IReadOnlyList<Food> candidates, SubstitutionMethod method, int top = 3)
    {
        var pool = candidates.Where(c => c.Id != missing.Id).ToList();
        if (pool.Count == 0) return Array.Empty<Substitution>();

        switch (method)
        {
            case SubstitutionMethod.Random:
                return pool.OrderBy(_ => _rnd.Next()).Take(top)
                    .Select(c => new Substitution(c, Distance(missing, c))).ToList();

            case SubstitutionMethod.Macro:
                return Rank(missing, pool, top);

            default:
                var sameCategory = pool.Where(c => c.Category == missing.Category).ToList();
                return Rank(missing, sameCategory.Count > 0 ? sameCategory : pool, top);
        }
    }

    private IReadOnlyList<Substitution> Rank(Food missing, List<Food> pool, int top) =>
        pool.Select(c => new Substitution(c, Distance(missing, c)))
            .OrderBy(s => s.Distance)
            .ThenBy(s => s.Candidate.Name, StringComparer.Ordinal)
            .Take(top)
            .ToList();

    private static double Per100(Food f, int k) => k switch
    {
        0 => f.KcalPer100,
        1 => f.ProteinPer100,
        2 => f.FatPer100,
        _ => f.CarbsPer100
    };
}
