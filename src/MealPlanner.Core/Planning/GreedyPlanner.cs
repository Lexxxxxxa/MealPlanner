using System.Diagnostics;
using MealPlanner.Core.Models;

namespace MealPlanner.Core.Planning;

/// <summary>
/// Greedy algorithm: at each step adds a portion of <see cref="StepGrams"/> of the product
/// with the best ratio of nutrient deficit closure to cost,
/// without violating upper bounds; complexity is O(S*n), where S is the number of steps and n the number of products
/// </summary>
public class GreedyPlanner : IMenuPlanner
{
    public string Name => "Greedy";

    public double StepGrams { get; init; } = 25;

    public MenuPlan Plan(IReadOnlyList<Food> foods, NutrientTargets targets)
    {
        var sw = Stopwatch.StartNew();
        int n = foods.Count;
        var grams = new double[n];
        var totals = new double[NutrientTargets.Dimensions];

        while (true)
        {
            int best = -1;
            double bestScore = 0;

            for (int i = 0; i < n; i++)
            {
                var food = foods[i];
                if (grams[i] + StepGrams > food.MaxGrams) continue;

                var add = food.NutrientsFor(StepGrams);
                if (ExceedsMax(totals, add, targets)) continue;

                double gain = 0;
                for (int k = 0; k < NutrientTargets.Dimensions; k++)
                {
                    double deficit = Math.Max(0, targets.Min(k) - totals[k]);
                    gain += Math.Min(deficit, add[k]) / targets.Min(k);
                }
                if (gain <= 0) continue;

                double score = gain / (food.CostFor(StepGrams) + 1e-9);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            if (best < 0) break;

            grams[best] += StepGrams;
            var a = foods[best].NutrientsFor(StepGrams);
            for (int k = 0; k < NutrientTargets.Dimensions; k++) totals[k] += a[k];
        }

        sw.Stop();

        var items = new List<MenuItem>();
        for (int i = 0; i < n; i++)
            if (grams[i] > 0) items.Add(new MenuItem(foods[i], grams[i]));

        var plan = new MenuPlan
        {
            Algorithm = Name,
            Items = items,
            ElapsedMs = sw.Elapsed.TotalMilliseconds
        };
        return Finalize(plan, targets);
    }

    private static bool ExceedsMax(double[] totals, Nutrients add, NutrientTargets t)
    {
        for (int k = 0; k < NutrientTargets.Dimensions; k++)
            if (totals[k] + add[k] > t.Max(k)) return true;
        return false;
    }

    private static MenuPlan Finalize(MenuPlan plan, NutrientTargets t)
    {
        bool ok = plan.Deviation(t) < 1e-9;
        return new MenuPlan
        {
            Algorithm = plan.Algorithm,
            Items = plan.Items,
            ElapsedMs = plan.ElapsedMs,
            IsFeasible = ok,
            Note = ok ? null : "Жадібний алгоритм не досяг усіх нижніх меж"
        };
    }
}
