using MealPlanner.Core.Models;

namespace MealPlanner.Api.Dtos;

public record IngredientInput(int FoodId, double Grams);

public record RecipeInput(string Name, int Servings, List<IngredientInput> Ingredients);

public record IngredientView(int FoodId, string FoodName, double Grams);

public record RecipeView(
    int Id, string Name, int Servings,
    Nutrients Total, Nutrients PerServing, double Cost,
    IReadOnlyList<IngredientView> Ingredients);
