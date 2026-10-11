using MealPlanner.Api.Data;
using MealPlanner.Api.Dtos;
using MealPlanner.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.Api.Endpoints;

public static class RecipeEndpoints
{
    public static void MapRecipeEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/recipes").WithTags("Recipes");

        g.MapGet("/", async (AppDbContext db) =>
        {
            var recipes = await db.Recipes.AsNoTracking()
                .Include(r => r.Ingredients).ThenInclude(i => i.Food)
                .OrderBy(r => r.Name)
                .ToListAsync();
            return Results.Ok(recipes.Select(ToView));
        });

        g.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var recipe = await db.Recipes.AsNoTracking()
                .Include(r => r.Ingredients).ThenInclude(i => i.Food)
                .FirstOrDefaultAsync(r => r.Id == id);
            return recipe is null ? Results.NotFound() : Results.Ok(ToView(recipe));
        });

        g.MapPost("/", async (RecipeInput input, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name) || input.Servings < 1)
                return Results.BadRequest("Потрібні назва та кількість порцій ≥ 1");
            if (input.Ingredients is null || input.Ingredients.Count == 0)
                return Results.BadRequest("Рецепт має містити інгредієнти");
            if (input.Ingredients.Any(i => i.Grams <= 0))
                return Results.BadRequest("Маса інгредієнта має бути додатною");

            var ids = input.Ingredients.Select(i => i.FoodId).Distinct().ToList();
            int found = await db.Foods.CountAsync(f => ids.Contains(f.Id));
            if (found != ids.Count) return Results.BadRequest("Невідомий продукт серед інгредієнтів");

            var recipe = new Recipe
            {
                Name = input.Name.Trim(),
                Servings = input.Servings,
                Ingredients = input.Ingredients
                    .Select(i => new RecipeIngredient { FoodId = i.FoodId, Grams = i.Grams })
                    .ToList()
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();

            await db.Entry(recipe).Collection(r => r.Ingredients).Query().Include(i => i.Food).LoadAsync();
            return Results.Created($"/api/recipes/{recipe.Id}", ToView(recipe));
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var recipe = await db.Recipes.FindAsync(id);
            if (recipe is null) return Results.NotFound();
            db.Recipes.Remove(recipe);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    internal static RecipeView ToView(Recipe r)
    {
        var total = r.Ingredients.Aggregate(Nutrients.Zero,
            (acc, i) => acc + i.Food!.NutrientsFor(i.Grams));
        double cost = r.Ingredients.Sum(i => i.Food!.CostFor(i.Grams));
        return new RecipeView(
            r.Id, r.Name, r.Servings,
            total, total * (1.0 / r.Servings), cost,
            r.Ingredients.Select(i => new IngredientView(i.FoodId, i.Food!.Name, i.Grams)).ToList());
    }
}
