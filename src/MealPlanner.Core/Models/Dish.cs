namespace MealPlanner.Core.Models;

public record DishIngredient(Food Food, double Grams)
{
    public Nutrients Nutrients => Food.NutrientsFor(Grams);
    public double Cost => Food.CostFor(Grams);
}

/// <summary>
/// Dish: name, servings, ingredients with mass, cooking time (minutes) and difficulty (1 easy, 2 medium, 3 hard)
/// </summary>
public record Dish(string Name, int Servings, IReadOnlyList<DishIngredient> Ingredients, int CookMinutes = 30, int Difficulty = 2)
{
    public Nutrients Total => Ingredients.Aggregate(Nutrients.Zero, (acc, i) => acc + i.Nutrients);
    public Nutrients PerServing => Total * (1.0 / Math.Max(1, Servings));
    public double Cost => Ingredients.Sum(i => i.Cost);
}

/// <summary>
/// Stock of a product in the household, grams
/// </summary>
public record PantryItem(Food Food, double Grams);
