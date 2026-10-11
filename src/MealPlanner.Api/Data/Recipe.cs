using MealPlanner.Core.Models;

namespace MealPlanner.Api.Data;

public class Recipe
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Servings { get; set; } = 1;
    public List<RecipeIngredient> Ingredients { get; set; } = new();
}

public class RecipeIngredient
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public Recipe? Recipe { get; set; }
    public int FoodId { get; set; }
    public Food? Food { get; set; }
    public double Grams { get; set; }
}
