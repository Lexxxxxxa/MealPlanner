using MealPlanner.Core.Models;

namespace MealPlanner.Core.Data;

/// <summary>
/// Dishes of the built-in database as ideas for selection by available products
/// </summary>
public static class TemplateDishes
{
    public static IReadOnlyList<Dish> Build(IReadOnlyList<Food> foods) =>
        DishCatalog.Build(foods).Select(c => c.Dish).ToList();
}
