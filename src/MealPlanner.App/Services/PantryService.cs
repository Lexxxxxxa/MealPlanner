using MealPlanner.App.Data;
using MealPlanner.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.App.Services;

public interface IPantryService
{
    Task<Dictionary<int, double>> LoadAsync(int householdId, CancellationToken ct);

    Task<List<PantryEntry>> ListAsync(int householdId, CancellationToken ct);

    Task ChangeAsync(int householdId, IEnumerable<(int FoodId, double Grams)> items, bool replace, CancellationToken ct);

    Task ClearAsync(int householdId, CancellationToken ct);

    Task ConsumeAsync(int householdId, Dish dish, double portions, CancellationToken ct);
}

/// <summary>
/// Product stock of the household
/// </summary>
public class PantryService : IPantryService
{
    private readonly AppDbContext _db;

    public PantryService(AppDbContext db) => _db = db;

    public Task<Dictionary<int, double>> LoadAsync(int householdId, CancellationToken ct) =>
        _db.Pantry.AsNoTracking()
            .Where(p => p.HouseholdId == householdId && p.Grams > 0)
            .ToDictionaryAsync(p => p.FoodId, p => p.Grams, ct);

    public Task<List<PantryEntry>> ListAsync(int householdId, CancellationToken ct) =>
        _db.Pantry.AsNoTracking().Include(p => p.Food)
            .Where(p => p.HouseholdId == householdId && p.Grams > 0)
            .OrderBy(p => p.Food!.Name)
            .ToListAsync(ct);

    public async Task ChangeAsync(
        int householdId, IEnumerable<(int FoodId, double Grams)> items, bool replace, CancellationToken ct)
    {
        var entries = await _db.Pantry.Where(p => p.HouseholdId == householdId).ToListAsync(ct);
        foreach (var (foodId, grams) in items)
        {
            var entry = entries.FirstOrDefault(e => e.FoodId == foodId);
            if (entry is null)
            {
                entry = new PantryEntry { HouseholdId = householdId, FoodId = foodId };
                _db.Pantry.Add(entry);
                entries.Add(entry);
            }
            entry.Grams = replace ? grams : entry.Grams + grams;
        }

        foreach (var empty in entries.Where(e => e.Grams <= 0).ToList())
        {
            if (_db.Entry(empty).State == EntityState.Added) _db.Entry(empty).State = EntityState.Detached;
            else _db.Pantry.Remove(empty);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task ClearAsync(int householdId, CancellationToken ct)
    {
        _db.Pantry.RemoveRange(_db.Pantry.Where(p => p.HouseholdId == householdId));
        await _db.SaveChangesAsync(ct);
    }

    public async Task ConsumeAsync(int householdId, Dish dish, double portions, CancellationToken ct)
    {
        double factor = portions / dish.Servings;
        var entries = await _db.Pantry.Where(e => e.HouseholdId == householdId).ToListAsync(ct);
        foreach (var ingredient in dish.Ingredients)
        {
            var entry = entries.FirstOrDefault(e => e.FoodId == ingredient.Food.Id);
            if (entry is null) continue;
            entry.Grams -= ingredient.Grams * factor;
            if (entry.Grams <= 0) _db.Pantry.Remove(entry);
        }
        await _db.SaveChangesAsync(ct);
    }
}
