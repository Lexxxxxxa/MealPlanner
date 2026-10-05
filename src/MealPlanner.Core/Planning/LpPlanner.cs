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
            constraints.Add(new LinearConstraint(row, Relation.GreaterOrEqual, targets.Min(k)));
            constraints.Add(new LinearConstraint((double[])row.Clone(), Relation.LessOrEqual, targets.Max(k)));
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
        if (result.Status == LpStatus.Optimal)
        {
            for (int i = 0; i < n; i++)
            {
                double grams = result.X[i] * 100.0;
                if (grams >= MinReportedGrams) items.Add(new MenuItem(foods[i], grams));
            }
        }

        var plan = new MenuPlan
        {
            Algorithm = Name,
            Items = items,
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
            Note = result.Status == LpStatus.Optimal ? null : $"Симплекс-метод: {result.Status}"
        };

        return new MenuPlan
        {
            Algorithm = plan.Algorithm,
            Items = plan.Items,
            ElapsedMs = plan.ElapsedMs,
            Note = plan.Note,
            IsFeasible = result.Status == LpStatus.Optimal && plan.Deviation(targets) < 1e-3
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
