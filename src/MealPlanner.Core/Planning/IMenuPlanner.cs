using MealPlanner.Core.Models;

namespace MealPlanner.Core.Planning;

/// <summary>
/// Algorithm that selects a daily menu under given constraints
/// </summary>
public interface IMenuPlanner
{
    string Name { get; }

    MenuPlan Plan(IReadOnlyList<Food> foods, NutrientTargets targets);
}
