using System.Globalization;
using System.Text;
using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Planning;

namespace MealPlanner.Benchmark.Experiments;

/// <summary>
/// Experiment 0: greedy algorithm versus linear programming on catalogs of different size
/// </summary>
public static class MenuExperiment
{
    public static void Run(int repeats, string outDir)
    {
        int[] sizes = { 59, 100, 200, 500, 1000 };
        var targets = NutrientTargets.Default2000;
        IMenuPlanner[] planners = { new GreedyPlanner(), new LpPlanner() };
        var inv = CultureInfo.InvariantCulture;

        var csv = new StringBuilder("n,seed,algorithm,feasible,cost_eur,kcal,protein,fat,carbs,deviation,time_ms,items\n");
        foreach (var p in planners) p.Plan(FoodCatalog.Generate(59), targets);

        Console.WriteLine($"\n== Експеримент 0: підбір меню з повного каталогу ({repeats} повторів) ==");
        Console.WriteLine($"{"n",5} {"algorithm",-18} {"feasible",8} {"cost",8} {"time ms",10}");
        foreach (int n in sizes)
        {
            var perAlgo = planners.ToDictionary(p => p.Name, _ => new List<MenuPlan>());
            for (int seed = 1; seed <= repeats; seed++)
            {
                var foods = FoodCatalog.Generate(n, seed);
                foreach (var planner in planners)
                {
                    var plan = planner.Plan(foods, targets);
                    perAlgo[planner.Name].Add(plan);
                    var t = plan.Totals;
                    csv.AppendLine(string.Join(",",
                        n, seed, plan.Algorithm, plan.IsFeasible ? 1 : 0,
                        plan.TotalCost.ToString("F4", inv),
                        t.Kcal.ToString("F1", inv), t.Protein.ToString("F1", inv),
                        t.Fat.ToString("F1", inv), t.Carbs.ToString("F1", inv),
                        plan.Deviation(targets).ToString("F5", inv),
                        plan.ElapsedMs.ToString("F3", inv),
                        plan.Items.Count));
                }
            }

            foreach (var (name, plans) in perAlgo)
            {
                var ok = plans.Where(p => p.IsFeasible).ToList();
                double cost = ok.Count > 0 ? ok.Average(p => p.TotalCost) : double.NaN;
                Console.WriteLine($"{n,5} {name,-18} {ok.Count + "/" + plans.Count,8} {cost.ToString("F3", inv),8} {plans.Average(p => p.ElapsedMs).ToString("F2", inv),10}");
            }
        }

        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "exp0_menu_full_catalogue.csv"), csv.ToString());
    }
}
