using MealPlanner.Api.Data;
using MealPlanner.Api.Dtos;
using MealPlanner.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.Api.Endpoints;

public static class FoodEndpoints
{
    public static void MapFoodEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/foods").WithTags("Foods");

        g.MapGet("/", async (AppDbContext db, string? category) =>
        {
            var q = db.Foods.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(category)) q = q.Where(f => f.Category == category);
            return Results.Ok(await q.OrderBy(f => f.Name).ToListAsync());
        });

        g.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            await db.Foods.FindAsync(id) is { } f ? Results.Ok(f) : Results.NotFound());

        g.MapPost("/", async (FoodInput input, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name))
                return Results.BadRequest("Назва продукту обов'язкова");
            if (input.PricePer100 <= 0 || input.MaxGrams <= 0)
                return Results.BadRequest("Ціна та максимальна порція мають бути додатними");

            var food = new Food
            {
                Name = input.Name.Trim(),
                Category = input.Category,
                KcalPer100 = input.KcalPer100,
                ProteinPer100 = input.ProteinPer100,
                FatPer100 = input.FatPer100,
                CarbsPer100 = input.CarbsPer100,
                PricePer100 = input.PricePer100,
                MaxGrams = input.MaxGrams
            };
            db.Foods.Add(food);
            await db.SaveChangesAsync();
            return Results.Created($"/api/foods/{food.Id}", food);
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var food = await db.Foods.FindAsync(id);
            if (food is null) return Results.NotFound();
            if (await db.RecipeIngredients.AnyAsync(i => i.FoodId == id))
                return Results.Conflict("Продукт використовується в рецептах");
            db.Foods.Remove(food);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
