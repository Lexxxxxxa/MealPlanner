using MealPlanner.Core.Models;

namespace MealPlanner.Core.Recommendation;

public record DishRecommendation(Dish Dish, double Score, CoverageInfo Coverage);

/// <summary>
/// Request context: target calories of a portion (optional)
/// </summary>
public record RecommendationContext(double? TargetKcalPerServing = null);

public interface IDishRecommender
{
    string Name { get; }

    IReadOnlyList<DishRecommendation> Recommend(
        IReadOnlyList<Dish> dishes,
        IReadOnlyDictionary<int, double> pantry,
        RecommendationContext context,
        int top);
}
