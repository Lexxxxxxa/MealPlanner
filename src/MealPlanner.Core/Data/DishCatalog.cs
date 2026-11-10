using System.Globalization;
using MealPlanner.Core.Models;
using MealPlanner.Core.Parsing;

namespace MealPlanner.Core.Data;

public record CatalogDish(string Category, Dish Dish);

/// <summary>
/// Built-in dish database: category, name, time, difficulty, servings and composition with ingredient mass
/// </summary>
public static class DishCatalog
{
    private record Raw(string Category, string Name, int Minutes, int Difficulty, int Servings, IReadOnlyList<(string Product, double Grams)> Items);

    private static readonly Lazy<IReadOnlyList<Raw>> Definitions = new(LoadRaw);

    public static int Count => Definitions.Value.Count;

    public static IReadOnlyList<string> Categories =>
        Definitions.Value.Select(r => r.Category).Distinct().ToList();

    public static IReadOnlyList<CatalogDish> Build(IReadOnlyList<Food> foods)
    {
        var map = new Dictionary<string, Food>();
        foreach (var f in foods) map[FoodMatcher.Normalize(f.Name)] = f;

        var result = new List<CatalogDish>();
        foreach (var raw in Definitions.Value)
        {
            var ingredients = new List<DishIngredient>();
            foreach (var (product, grams) in raw.Items)
                if (map.TryGetValue(FoodMatcher.Normalize(product), out var food))
                    ingredients.Add(new DishIngredient(food, grams));
            if (ingredients.Count == raw.Items.Count)
                result.Add(new CatalogDish(raw.Category, new Dish(raw.Name, raw.Servings, ingredients, raw.Minutes, raw.Difficulty)));
        }
        return result;
    }

    public static IReadOnlyList<string> UnresolvedProducts(IReadOnlyList<Food> foods)
    {
        var names = foods.Select(f => FoodMatcher.Normalize(f.Name)).ToHashSet();
        return Definitions.Value.SelectMany(r => r.Items).Select(i => i.Product)
            .Where(p => !names.Contains(FoodMatcher.Normalize(p))).Distinct().ToList();
    }

    private static IReadOnlyList<Raw> LoadRaw()
    {
        var list = new List<Raw>();
        foreach (var line in FoodCatalog.ReadResource("dishes.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Trim().Split('|');
            if (p.Length != 6) continue;
            var items = p[5].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x =>
            {
                int i = x.LastIndexOf(':');
                return (x[..i], double.Parse(x[(i + 1)..], CultureInfo.InvariantCulture));
            }).ToList();
            list.Add(new Raw(p[0], p[1], int.Parse(p[2], CultureInfo.InvariantCulture), int.Parse(p[3], CultureInfo.InvariantCulture),
                int.Parse(p[4], CultureInfo.InvariantCulture), items));
        }
        return list;
    }
}
