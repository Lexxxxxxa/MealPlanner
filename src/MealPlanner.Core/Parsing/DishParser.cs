using System.Globalization;
using System.Text.RegularExpressions;
using MealPlanner.Core.Models;

namespace MealPlanner.Core.Parsing;

public record ParsedItems(List<PantryItem> Items, List<string> UnknownFoods, List<string> Errors);

public record ParsedDish(string Name, int Servings, List<DishIngredient> Ingredients, List<string> UnknownFoods, List<string> Errors)
{
    public bool IsValid => Errors.Count == 0 && UnknownFoods.Count == 0 && Ingredients.Count > 0;
}

/// <summary>
/// Parsing of a text dish description of the form "name: product amount, product amount; N servings"
/// Supported units: g, kg, ml, l, pcs (for eggs and common vegetables and fruits)
/// </summary>
public static class DishParser
{
    private static readonly Regex ServingsRx = new(@"(\d+)\s*порц\w*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ItemRx = new(
        @"^(?<name>.+?)\s+(?<num>\d+(?:[.,]\d+)?)\s*(?<unit>[а-яa-z]*)\.?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, double> PieceGrams = new()
    {
        ["яйца"] = 55, ["банан"] = 120, ["яблоко"] = 150, ["апельсин"] = 150,
        ["лук репчатый"] = 80, ["морковь"] = 80, ["помидоры"] = 120, ["огурцы"] = 120,
        ["картофель"] = 100, ["чеснок"] = 5, ["перец болгарский"] = 150
    };

    public static ParsedDish Parse(string text, IReadOnlyList<Food> foods)
    {
        var errors = new List<string>();
        var unknown = new List<string>();
        var ingredients = new List<DishIngredient>();

        int colon = text.IndexOf(':');
        if (colon <= 0)
            return new ParsedDish(string.Empty, 1, ingredients, unknown,
                new() { "Формат: название: продукт граммы, продукт граммы; N порций" });

        string name = text[..colon].Trim();
        string rest = text[(colon + 1)..];

        int servings = 1;
        var sm = ServingsRx.Match(rest);
        if (sm.Success)
        {
            servings = int.Parse(sm.Groups[1].Value, CultureInfo.InvariantCulture);
            rest = rest.Remove(sm.Index, sm.Length);
        }
        if (servings < 1) errors.Add("Количество порций должно быть не меньше 1");

        var items = ParseItems(rest, foods);
        ingredients.AddRange(items.Items.Select(i => new DishIngredient(i.Food, i.Grams)));
        unknown.AddRange(items.UnknownFoods);
        errors.AddRange(items.Errors);

        if (ingredients.Count == 0 && errors.Count == 0 && unknown.Count == 0)
            errors.Add("В блюде нет ингредиентов");

        return new ParsedDish(name, Math.Max(1, servings), ingredients, unknown, errors);
    }

    public static ParsedItems ParseItems(string text, IReadOnlyList<Food> foods)
    {
        var items = new List<PantryItem>();
        var unknown = new List<string>();
        var errors = new List<string>();

        foreach (var raw in Regex.Split(text, @";|\n|,(?!\d)"))
        {
            string item = raw.Trim();
            if (item.Length == 0) continue;

            var m = ItemRx.Match(item);
            if (!m.Success)
            {
                errors.Add($"Не удалось разобрать: «{item}» (нужно: название и количество)");
                continue;
            }

            string foodName = m.Groups["name"].Value.Trim();
            double qty = double.Parse(m.Groups["num"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            string unit = m.Groups["unit"].Value.ToLowerInvariant();

            var food = FoodMatcher.Find(foodName, foods);
            if (food is null)
            {
                unknown.Add(foodName);
                continue;
            }

            double? grams = ToGrams(qty, unit, food);
            if (grams is null || grams <= 0)
            {
                errors.Add($"Неизвестная единица измерения «{unit}» для «{foodName}»");
                continue;
            }
            items.Add(new PantryItem(food, grams.Value));
        }
        return new ParsedItems(items, unknown, errors);
    }

    public static double? ToGrams(double qty, string unit, Food food)
    {
        switch (unit)
        {
            case "":
            case "г":
            case "гр":
            case "грамм":
            case "грамма":
            case "граммов":
            case "g":
            case "мл":
                return qty;
            case "кг":
                return qty * 1000;
            case "л":
                return qty * 1000;
            case "шт":
            case "штук":
            case "штуки":
                return PieceGrams.TryGetValue(FoodMatcher.Normalize(food.Name), out var g) ? qty * g : null;
            default:
                return null;
        }
    }
}
