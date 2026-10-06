using System.Globalization;
using MealPlanner.Core.Models;

namespace MealPlanner.Core.Data;

/// <summary>
/// Product catalog: loading of the initial set from the embedded CSV
/// and generation of scaled sets for experiments
/// Nutrition values and prices are approximate (averaged reference data)
/// </summary>
public static class FoodCatalog
{
    public static IReadOnlyList<Food> LoadSeed()
    {
        var asm = typeof(FoodCatalog).Assembly;
        string resource = asm.GetManifestResourceNames().First(n => n.EndsWith("foods.csv"));
        using var stream = asm.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static IReadOnlyList<Food> Parse(string csv)
    {
        var foods = new List<Food>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < lines.Length; i++)
        {
            var p = lines[i].Trim().Split(',');
            if (p.Length < 8) continue;
            foods.Add(new Food
            {
                Id = foods.Count + 1,
                Name = p[0],
                Category = p[1],
                KcalPer100 = Num(p[2]),
                ProteinPer100 = Num(p[3]),
                FatPer100 = Num(p[4]),
                CarbsPer100 = Num(p[5]),
                PricePer100 = Num(p[6]),
                MaxGrams = Num(p[7])
            });
        }
        return foods;
    }

    public static IReadOnlyList<Food> Generate(int count, int seed = 42)
    {
        var baseFoods = LoadSeed();
        if (count <= baseFoods.Count) return baseFoods.Take(count).ToList();

        var rnd = new Random(seed);
        var result = new List<Food>(baseFoods);
        for (int i = baseFoods.Count; i < count; i++)
        {
            var s = baseFoods[i % baseFoods.Count];
            double f() => 0.8 + rnd.NextDouble() * 0.4;
            result.Add(new Food
            {
                Id = i + 1,
                Name = $"{s.Name} #{i + 1}",
                Category = s.Category,
                KcalPer100 = s.KcalPer100 * f(),
                ProteinPer100 = s.ProteinPer100 * f(),
                FatPer100 = s.FatPer100 * f(),
                CarbsPer100 = s.CarbsPer100 * f(),
                PricePer100 = s.PricePer100 * (0.7 + rnd.NextDouble() * 0.8),
                MaxGrams = s.MaxGrams
            });
        }
        return result;
    }

    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}
