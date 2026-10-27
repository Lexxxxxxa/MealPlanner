using System.Diagnostics;
using MealPlanner.Core.Models;
using MealPlanner.Core.Optimization;

namespace MealPlanner.Core.Planning;

/// <summary>
/// Menu selection as a linear programming problem (diet problem):
/// min sum p_i*x_i, where x_i is the mass of product i in hundreds of grams, subject to
/// min_k <= sum a_ik*x_i <= max_k for each nutrient k and 0 <= x_i <= MaxGrams_i/100
/// </summary>
public class LpPlanner : IMenuPlanner
{
    private const double MinReportedGrams = 0.5;

    private const double SafetyMargin = 0.005;

    public string Name => "LinearProgramming";

    public MenuPlan Plan(IReadOnlyList<Food> foods, NutrientTargets targets)
    {
        var sw = Stopwatch.StartNew();
        int n = foods.Count;

        var cost = new double[n];
        for (int i = 0; i < n; i++) cost[i] = foods[i].PricePer100;

        var constraints = new List<LinearConstraint>();
        for (int k = 0; k < NutrientTargets.Dimensions; k++)
        {
            var row = new double[n];
            for (int i = 0; i < n; i++) row[i] = PerHundred(foods[i], k);
            double lo = targets.Min(k) * (1 + SafetyMargin);
            double hi = targets.Max(k) * (1 - SafetyMargin);
            if (lo > hi) { lo = targets.Min(k); hi = targets.Max(k); }
            constraints.Add(new LinearConstraint(row, Relation.GreaterOrEqual, lo));
            constraints.Add(new LinearConstraint((double[])row.Clone(), Relation.LessOrEqual, hi));
        }
        for (int i = 0; i < n; i++)
        {
            var row = new double[n];
            row[i] = 1;
            constraints.Add(new LinearConstraint(row, Relation.LessOrEqual, foods[i].MaxGrams / 100.0));
        }

        var result = new SimplexSolver().Minimize(cost, constraints);
        sw.Stop();

        var items = new List<MenuItem>();
        MenuPlan plan = Make(items, sw, result.Status, targets);
        if (result.Status == LpStatus.Optimal)
        {
            items = Collect(foods, result, MinReportedGrams);
            plan = Make(items, sw, result.Status, targets);
            if (plan.Deviation(targets) > StrictTolerance)
            {
                items = Collect(foods, result, 1e-6);
                plan = Make(items, sw, result.Status, targets);
            }
        }
        return plan;
    }

    private const double StrictTolerance = 1e-6;

    private static List<MenuItem> Collect(IReadOnlyList<Food> foods, LpResult result, double minGrams)
    {
        var items = new List<MenuItem>();
        for (int i = 0; i < foods.Count; i++)
        {
            double grams = result.X[i] * 100.0;
            if (grams >= minGrams) items.Add(new MenuItem(foods[i], grams));
        }
        return items;
    }

    private MenuPlan Make(List<MenuItem> items, Stopwatch sw, LpStatus status, NutrientTargets targets)
    {
        var plan = new MenuPlan
        {
            Algorithm = Name,
            Items = items,
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
            Note = status == LpStatus.Optimal ? null : $"Симплекс-метод: {status}"
        };
        return new MenuPlan
        {
            Algorithm = plan.Algorithm,
            Items = plan.Items,
            ElapsedMs = plan.ElapsedMs,
            Note = plan.Note,
            IsFeasible = status == LpStatus.Optimal && plan.Deviation(targets) <= StrictTolerance
        };
    }

    private static double PerHundred(Food f, int k) => k switch
    {
        0 => f.KcalPer100,
        1 => f.ProteinPer100,
        2 => f.FatPer100,
        _ => f.CarbsPer100
    };
}
