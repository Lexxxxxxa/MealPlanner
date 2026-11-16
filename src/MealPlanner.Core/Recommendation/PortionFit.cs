using MealPlanner.Core.Models;

namespace MealPlanner.Core.Recommendation;

/// <summary>
/// Estimate of how well a portion closes the remaining calories and macros of the day (lower is better)
/// score = |dKcal| / remaining + sum over P,F,C of (0.5*excess + 0.25*shortage) / max(nutrient remaining, 10)
/// </summary>
public static class PortionFit
{
    public static Nutrients Clip(Nutrients n) =>
        new(Math.Max(0, n.Kcal), Math.Max(0, n.Protein), Math.Max(0, n.Fat), Math.Max(0, n.Carbs));

    public static double Score(Nutrients portion, Nutrients remaining)
    {
        double score = Math.Abs(portion.Kcal - remaining.Kcal) / Math.Max(remaining.Kcal, 1);
        for (int k = 1; k <= 3; k++)
        {
            double rem = remaining[k], got = portion[k], scale = Math.Max(rem, 10);
            score += 0.5 * Math.Max(0, got - rem) / scale + 0.25 * Math.Max(0, rem - got) / scale;
        }
        return score;
    }
}
