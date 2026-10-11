using MealPlanner.Core.Models;

namespace MealPlanner.Api.Dtos;

/// <summary>
/// Menu selection request; Algorithm is "greedy" or "lp"; Targets are optional (2000 kcal by default)
/// </summary>
public record PlanRequest(string Algorithm, NutrientTargets? Targets);

public record PlanItemView(int FoodId, string Name, double Grams, double Cost);

public record PlanView(
    string Algorithm, bool IsFeasible, double TotalCost, Nutrients Totals,
    double ElapsedMs, string? Note, IReadOnlyList<PlanItemView> Items);

public record ShoppingRecipeInput(int RecipeId, int Servings);

public record ShoppingListRequest(List<IngredientInput>? Items, List<ShoppingRecipeInput>? Recipes);

public record ShoppingLine(int FoodId, string Name, double Grams, double Cost);

public record ShoppingListView(IReadOnlyList<ShoppingLine> Lines, double TotalCost);
