using System.Globalization;
using System.Text;
using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Planning;

int repeats = args.Length > 0 && int.TryParse(args[0], out var r) ? r : 10;
string outPath = args.Length > 1 ? args[1] : Path.Combine("results", "benchmark.csv");
int[] sizes = { 43, 100, 200, 500, 1000 };

var targets = NutrientTargets.Default2000;
IMenuPlanner[] planners = { new GreedyPlanner(), new LpPlanner() };
var inv = CultureInfo.InvariantCulture;

var csv = new StringBuilder();
csv.AppendLine("n,seed,algorithm,feasible,cost_eur,kcal,protein,fat,carbs,deviation,time_ms,items");

foreach (var p in planners) p.Plan(FoodCatalog.Generate(43), targets);

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
        int feasible = plans.Count(p => p.IsFeasible);
        var ok = plans.Where(p => p.IsFeasible).ToList();
        double cost = ok.Count > 0 ? ok.Average(p => p.TotalCost) : double.NaN;
        double ms = plans.Average(p => p.ElapsedMs);
        Console.WriteLine($"{n,5} {name,-18} {feasible + "/" + plans.Count,8} {cost.ToString("F3", inv),8} {ms.ToString("F2", inv),10}");
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
File.WriteAllText(outPath, csv.ToString());
Console.WriteLine($"\nРезультати збережено: {outPath}");
