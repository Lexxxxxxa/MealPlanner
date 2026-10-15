using System.Text;
using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Recommendation;

namespace MealPlanner.Benchmark.Experiments;

/// <summary>
/// Experiment 2: quality of substitution for a missing ingredient
/// For a random (dish, ingredient) pair the ingredient is removed and a replacement of the same mass
/// is chosen among 12 random available products; deviations of calories and macros of the dish
/// from the original are compared; Oracle is the best possible replacement among candidates (upper bound)
/// </summary>
public static class SubstitutionExperiment
{
    private const int PoolSize = 12;

    public static void Run(int trials, int dishCount, string outDir)
    {
        var foods = FoodCatalog.LoadSeed();
        var dishes = SyntheticData.GenerateDishes(foods, dishCount, seed: 100);
        var finder = new SubstitutionFinder(foods, seed: 5);
        var rnd = new Random(77);

        string[] methods = { "Random", "Macro", "CategoryMacro", "Oracle" };
        var kcalErr = methods.ToDictionary(m => m, _ => new List<double>());
        var macroErr = methods.ToDictionary(m => m, _ => new List<double>());
        var within10 = methods.ToDictionary(m => m, _ => new List<double>());
        var sameCat = methods.ToDictionary(m => m, _ => new List<double>());

        for (int t = 0; t < trials; t++)
        {
            var dish = dishes[rnd.Next(dishes.Count)];
            if (dish.Ingredients.Count < 2) { t--; continue; }

            var removed = dish.Ingredients[rnd.Next(dish.Ingredients.Count)];
            var usedIds = dish.Ingredients.Select(i => i.Food.Id).ToHashSet();
            var pool = foods.Where(f => !usedIds.Contains(f.Id)).OrderBy(_ => rnd.Next()).Take(PoolSize).ToList();

            foreach (var m in methods)
            {
                Food pick = m switch
                {
                    "Random" => finder.Suggest(removed.Food, pool, SubstitutionMethod.Random, 1)[0].Candidate,
                    "Macro" => finder.Suggest(removed.Food, pool, SubstitutionMethod.Macro, 1)[0].Candidate,
                    "CategoryMacro" => finder.Suggest(removed.Food, pool, SubstitutionMethod.CategoryMacro, 1)[0].Candidate,
                    _ => pool.OrderBy(c => MacroError(dish, removed, c)).First()
                };

                double e = MacroError(dish, removed, pick);
                double k = KcalError(dish, removed, pick);
                kcalErr[m].Add(k);
                macroErr[m].Add(e);
                within10[m].Add(k <= 0.10 ? 1 : 0);
                sameCat[m].Add(pick.Category == removed.Food.Category ? 1 : 0);
            }
        }

        var summary = new StringBuilder("method,kcal_err,kcal_err_ci95,macro_err,within_10pct,same_category\n");
        Console.WriteLine($"\n== Експеримент 2: підстановка інгредієнтів ({trials} випробувань, пул {PoolSize} продуктів) ==");
        Console.WriteLine($"{"method",-15} {"kcalErr",8} {"±CI95",7} {"macroErr",9} {"≤10%",7} {"sameCat",8}");
        foreach (var m in methods)
        {
            Console.WriteLine($"{m,-15} {Stats.F(Stats.Mean(kcalErr[m])),8} {Stats.F(Stats.Ci95(kcalErr[m])),7} " +
                              $"{Stats.F(Stats.Mean(macroErr[m])),9} {Stats.F(Stats.Mean(within10[m])),7} {Stats.F(Stats.Mean(sameCat[m])),8}");
            summary.AppendLine(string.Join(",", m, Stats.F(Stats.Mean(kcalErr[m]), 4), Stats.F(Stats.Ci95(kcalErr[m]), 4),
                Stats.F(Stats.Mean(macroErr[m]), 4), Stats.F(Stats.Mean(within10[m]), 4), Stats.F(Stats.Mean(sameCat[m]), 4)));
        }

        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "exp2_substitution_summary.csv"), summary.ToString());
    }

    private static double KcalError(Dish dish, DishIngredient removed, Food sub)
    {
        double orig = dish.Total.Kcal;
        double now = orig - removed.Nutrients.Kcal + sub.NutrientsFor(removed.Grams).Kcal;
        return Math.Abs(now - orig) / orig;
    }

    private static double MacroError(Dish dish, DishIngredient removed, Food sub)
    {
        var a = removed.Nutrients;
        var b = sub.NutrientsFor(removed.Grams);
        double diff = Math.Abs(b.Protein - a.Protein) * 4 + Math.Abs(b.Fat - a.Fat) * 9 + Math.Abs(b.Carbs - a.Carbs) * 4;
        return diff / dish.Total.Kcal;
    }
}
