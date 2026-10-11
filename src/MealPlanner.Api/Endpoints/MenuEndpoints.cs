using MealPlanner.Api.Data;
using MealPlanner.Api.Dtos;
using MealPlanner.Core.Models;
using MealPlanner.Core.Planning;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.Api.Endpoints;

public static class MenuEndpoints
{
    public static void MapMenuEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("Menu");

        g.MapPost("/menu/plan", async (PlanRequest req, AppDbContext db) =>
        {
            IMenuPlanner? planner = req.Algorithm?.ToLowerInvariant() switch
            {
                "greedy" => new GreedyPlanner(),
                "lp" => new LpPlanner(),
                _ => null
            };
            if (planner is null) return Results.BadRequest("Algorithm має бути 'greedy' або 'lp'");

            var foods = await db.Foods.AsNoTracking().ToListAsync();
            if (foods.Count == 0) return Results.BadRequest("Каталог продуктів порожній");

            var plan = planner.Plan(foods, req.Targets ?? NutrientTargets.Default2000);
            return Results.Ok(ToView(plan));
        });

        g.MapPost("/shopping-list", async (ShoppingListRequest req, AppDbContext db) =>
        {
            var grams = new Dictionary<int, double>();

            foreach (var item in req.Items ?? new())
            {
                if (item.Grams <= 0) return Results.BadRequest("Маса має бути додатною");
                grams[item.FoodId] = grams.GetValueOrDefault(item.FoodId) + item.Grams;
            }

            foreach (var rr in req.Recipes ?? new())
            {
                if (rr.Servings < 1) return Results.BadRequest("Кількість порцій має бути ≥ 1");
                var recipe = await db.Recipes.AsNoTracking()
                    .Include(r => r.Ingredients)
                    .FirstOrDefaultAsync(r => r.Id == rr.RecipeId);
                if (recipe is null) return Results.BadRequest($"Рецепт {rr.RecipeId} не знайдено");

                double k = (double)rr.Servings / recipe.Servings;
                foreach (var ing in recipe.Ingredients)
                    grams[ing.FoodId] = grams.GetValueOrDefault(ing.FoodId) + ing.Grams * k;
            }

            var ids = grams.Keys.ToList();
            var foods = await db.Foods.AsNoTracking().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id);
            if (foods.Count != ids.Count) return Results.BadRequest("Невідомий продукт у списку");

            var lines = grams
                .Select(kv => new ShoppingLine(kv.Key, foods[kv.Key].Name, Math.Round(kv.Value, 1),
                    Math.Round(foods[kv.Key].CostFor(kv.Value), 2)))
                .OrderBy(l => l.Name)
                .ToList();
            return Results.Ok(new ShoppingListView(lines, Math.Round(lines.Sum(l => l.Cost), 2)));
        });
    }

    private static PlanView ToView(MenuPlan p) => new(
        p.Algorithm, p.IsFeasible, Math.Round(p.TotalCost, 4), p.Totals, p.ElapsedMs, p.Note,
        p.Items.Select(i => new PlanItemView(i.Food.Id, i.Food.Name, Math.Round(i.Grams, 1), Math.Round(i.Cost, 4))).ToList());
}
