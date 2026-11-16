using MealPlanner.App.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Parsing;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.App.Services;

public interface IFoodRepository
{
    Task<List<Food>> ListAsync(CancellationToken ct);

    Task<Food> AddAsync(Food food, CancellationToken ct);

    Task<(Food Food, bool Created)> UpsertAsync(Food values, CancellationToken ct);
}

/// <summary>
/// Product catalog in the database
/// </summary>
public class FoodRepository : IFoodRepository
{
    public const string CustomCategory = "Свои продукты";
    public const double DefaultPrice = 0.3;
    public const double DefaultMaxGrams = 400;

    private readonly AppDbContext _db;

    public FoodRepository(AppDbContext db) => _db = db;

    public Task<List<Food>> ListAsync(CancellationToken ct) => _db.Foods.AsNoTracking().ToListAsync(ct);

    public async Task<Food> AddAsync(Food food, CancellationToken ct)
    {
        _db.Foods.Add(food);
        await _db.SaveChangesAsync(ct);
        return food;
    }

    public async Task<(Food Food, bool Created)> UpsertAsync(Food values, CancellationToken ct)
    {
        string key = FoodMatcher.Normalize(values.Name);
        var all = await _db.Foods.ToListAsync(ct);
        var existing = all.FirstOrDefault(f => FoodMatcher.Normalize(f.Name) == key);
        if (existing is null)
        {
            _db.Foods.Add(values);
            await _db.SaveChangesAsync(ct);
            return (values, true);
        }

        existing.KcalPer100 = values.KcalPer100;
        existing.ProteinPer100 = values.ProteinPer100;
        existing.FatPer100 = values.FatPer100;
        existing.CarbsPer100 = values.CarbsPer100;
        if (values.PricePer100 > 0) existing.PricePer100 = values.PricePer100;
        await _db.SaveChangesAsync(ct);
        return (existing, false);
    }
}
