using System.Text;
using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Planning;

namespace MealPlanner.Benchmark.Experiments;

/// <summary>
/// Experiment 3: daily menu only from available stock, where the mass of each product is limited by its stock
/// Dependence of the share of feasible menus and of the cost on the number of distinct products in stock
/// </summary>
public static class PantryMenuExperiment
{
    public static void Run(int householdsPerSize, string outDir)
    {
        var catalogue = FoodCatalog.LoadSeed();
        var targets = NutrientTargets.Default2000;
        int[] sizes = { 10, 15, 20, 30, 45, 59 };
        IMenuPlanner[] planners = { new GreedyPlanner(), new LpPlanner() };

        var csv = new StringBuilder("pantry_size,algorithm,feasible_rate,mean_cost_eur,mean_time_ms\n");
        Console.WriteLine($"\n== Експеримент 3: меню з наявних запасів ({householdsPerSize} домогосподарств на розмір) ==");
        Console.WriteLine($"{"foods",6} {"algorithm",-18} {"feasible",9} {"cost EUR",9} {"ms",8}");

        var rnd = new Random(31);
        foreach (int size in sizes)
        {
            var pantries = new List<List<Food>>();
            for (int h = 0; h < householdsPerSize; h++)
            {
                pantries.Add(catalogue.OrderBy(_ => rnd.Next()).Take(size)
                    .Select(f => Clone(f, Math.Min(f.MaxGrams, 100 + rnd.NextDouble() * 700)))
                    .ToList());
            }

            foreach (var planner in planners)
            {
                var plans = pantries.Select(p => planner.Plan(p, targets)).ToList();
                var ok = plans.Where(p => p.IsFeasible).ToList();
                double rate = ok.Count / (double)plans.Count;
                double cost = ok.Count > 0 ? ok.Average(p => p.TotalCost) : double.NaN;
                double ms = plans.Average(p => p.ElapsedMs);

                Console.WriteLine($"{size,6} {planner.Name,-18} {Stats.F(rate),9} {Stats.F(cost, 2),9} {Stats.F(ms, 2),8}");
                csv.AppendLine(string.Join(",", size, planner.Name, Stats.F(rate, 4), Stats.F(cost, 4), Stats.F(ms, 4)));
            }
        }

        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "exp3_pantry_menu.csv"), csv.ToString());
    }

    private static Food Clone(Food f, double maxGrams) => new()
    {
        Id = f.Id, Name = f.Name, Category = f.Category,
        KcalPer100 = f.KcalPer100, ProteinPer100 = f.ProteinPer100,
        FatPer100 = f.FatPer100, CarbsPer100 = f.CarbsPer100,
        PricePer100 = f.PricePer100, MaxGrams = maxGrams
    };
}
