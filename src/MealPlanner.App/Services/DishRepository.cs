using MealPlanner.App.Data;
using MealPlanner.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.App.Services;

public record StoredDish(HouseholdDish Entity, Dish Model);

public interface IDishRepository
{
    Task<List<StoredDish>> ListAsync(int householdId, IReadOnlyList<Food> foods, CancellationToken ct);

    Task<bool> SaveAsync(int householdId, Dish dish, CancellationToken ct);

    Task<bool> UpdateAsync(int householdId, int dishId, Dish dish, CancellationToken ct);

    Task<string?> DeleteAsync(int householdId, int dishId, CancellationToken ct);
}

/// <summary>
/// Saved dishes of the household
/// </summary>
public class DishRepository : IDishRepository
{
    private readonly AppDbContext _db;

    public DishRepository(AppDbContext db) => _db = db;

    public async Task<List<StoredDish>> ListAsync(int householdId, IReadOnlyList<Food> foods, CancellationToken ct)
    {
        var map = foods.ToDictionary(f => f.Id);
        var entities = await _db.Dishes.AsNoTracking()
            .Include(d => d.Ingredients)
            .Where(d => d.HouseholdId == householdId)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        return entities.Select(e => new StoredDish(e, ToModel(e, map))).ToList();
    }

    public async Task<bool> SaveAsync(int householdId, Dish dish, CancellationToken ct)
    {
        var existing = await _db.Dishes.Where(d => d.HouseholdId == householdId).ToListAsync(ct);
        var same = existing.FirstOrDefault(d => d.Name.Equals(dish.Name, StringComparison.CurrentCultureIgnoreCase));
        if (same != null) _db.Dishes.Remove(same);

        var entity = new HouseholdDish { HouseholdId = householdId, Name = dish.Name };
        Apply(entity, dish);
        _db.Dishes.Add(entity);
        await _db.SaveChangesAsync(ct);
        return same != null;
    }

    public async Task<bool> UpdateAsync(int householdId, int dishId, Dish dish, CancellationToken ct)
    {
        var entity = await _db.Dishes.Include(d => d.Ingredients)
            .FirstOrDefaultAsync(d => d.Id == dishId && d.HouseholdId == householdId, ct);
        if (entity is null) return false;

        _db.DishIngredients.RemoveRange(entity.Ingredients);
        Apply(entity, dish);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<string?> DeleteAsync(int householdId, int dishId, CancellationToken ct)
    {
        var entity = await _db.Dishes.FirstOrDefaultAsync(d => d.Id == dishId && d.HouseholdId == householdId, ct);
        if (entity is null) return null;

        _db.Dishes.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return entity.Name;
    }

    private static void Apply(HouseholdDish entity, Dish dish)
    {
        entity.Servings = dish.Servings;
        entity.CookMinutes = dish.CookMinutes;
        entity.Difficulty = dish.Difficulty;
        entity.Instructions = dish.Instructions;
        entity.Ingredients = dish.Ingredients
            .GroupBy(i => i.Food.Id)
            .Select(g => new HouseholdDishIngredient { FoodId = g.Key, Grams = g.Sum(i => i.Grams) })
            .ToList();
    }

    private static Dish ToModel(HouseholdDish e, IReadOnlyDictionary<int, Food> foods) =>
        new(e.Name, e.Servings,
            e.Ingredients.Select(i => new DishIngredient(foods[i.FoodId], i.Grams)).ToList(),
            e.CookMinutes, e.Difficulty, e.Instructions);
}
