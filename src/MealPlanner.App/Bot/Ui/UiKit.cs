using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MealPlanner.App.Data;
using MealPlanner.App.Services;
using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Parsing;
using MealPlanner.Core.Recommendation;

namespace MealPlanner.App.Bot;

/// <summary>
/// Shared UI building blocks: keyboards, pagination, search, formatting
/// </summary>
public static class UiKit
{
    public static readonly string[] CategoryOrder =
    {
        "Мясо и рыба", "Молочные и яйца", "Овощи", "Крупы и хлеб",
        "Бобовые", "Фрукты", "Жиры и орехи", "Сладкое", "Свои продукты"
    };

    public static readonly Dictionary<string, string> CategoryEmoji = new()
    {
        ["Мясо и рыба"] = "🥩", ["Молочные и яйца"] = "🥛", ["Овощи"] = "🥦", ["Крупы и хлеб"] = "🍚",
        ["Бобовые"] = "🫘", ["Фрукты"] = "🍎", ["Жиры и орехи"] = "🥜", ["Сладкое"] = "🍯", ["Свои продукты"] = "⭐",
        ["Хлеб и выпечка"] = "🍞", ["Птица"] = "🍗", ["Рыба и морепродукты"] = "🐟", ["Колбасы и деликатесы"] = "🥓",
        ["Грибы"] = "🍄", ["Зелень и специи"] = "🌿", ["Соусы и приправы"] = "🧂", ["Напитки"] = "🥤", ["Консервы"] = "🥫",
        ["Завтраки"] = "🍳", ["Супы"] = "🍲", ["Салаты"] = "🥗", ["Мясные блюда"] = "🥩", ["Блюда из птицы"] = "🍗",
        ["Овощные блюда"] = "🥕", ["Каши и гарниры"] = "🥣", ["Паста и лапша"] = "🍝", ["Бобовые и тофу"] = "🫘",
        ["Выпечка"] = "🥐", ["Десерты"] = "🍰", ["Закуски и бутерброды"] = "🥪", ["Напитки и смузи"] = "🥤", ["Быстрые блюда"] = "⚡"
    };

    public static readonly int[] GramPresets = { 50, 100, 150, 200, 250, 300, 400, 500 };

    public static readonly int[] ServingPresets = { 1, 2, 3, 4, 5, 6, 8, 10 };

    public static List<string> Categories(IEnumerable<Food> foods) =>
        foods.Select(f => f.Category).Distinct()
            .OrderBy(c => { int i = Array.IndexOf(CategoryOrder, c); return i < 0 ? 99 : i; })
            .ToList();

    public static string CatLabel(string cat) =>
        (CategoryEmoji.TryGetValue(cat, out var e) ? e : "🍴") + " " + cat;

    public static List<IReadOnlyList<InlineButton>> Rows(IEnumerable<InlineButton> buttons, int perRow) =>
        buttons.Chunk(perRow).Select(chunk => (IReadOnlyList<InlineButton>)chunk.ToList()).ToList();

    public static IReadOnlyList<InlineButton> Row(params InlineButton[] b) => b.ToList();

    public static string Nutr(Nutrients n) => $"{n.Kcal:F0} ккал · Б {n.Protein:F0} / Ж {n.Fat:F0} / У {n.Carbs:F0}";

    public static bool PassesFilter(Session s, Dish d) =>
        (!s.QuickOnly || d.CookMinutes <= 20) && d.Difficulty <= s.MaxDifficulty;

    public static string DifficultyText(int d) => d switch { 1 => "легко", 3 => "сложно", _ => "средне" };

    public static string TimeInfo(Dish d) => $"⏱ {d.CookMinutes} мин · сложность: {DifficultyText(d.Difficulty)}";

    public static string FilterText(Session s)
    {
        var parts = new List<string>();
        if (s.QuickOnly) parts.Add("до 20 минут");
        if (s.MaxDifficulty == 1) parts.Add("только лёгкие");
        else if (s.MaxDifficulty == 2) parts.Add("не сложные");
        return string.Join(", ", parts);
    }

    public static IReadOnlyList<InlineButton> FilterRow(Session s, string prefix = "i") => Row(
        new InlineButton((s.QuickOnly ? "✅ " : "") + "⚡ До 20 мин", $"{prefix}:f:t"),
        new InlineButton(s.MaxDifficulty switch { 1 => "🔧 Только лёгкие", 2 => "🔧 Не сложные", _ => "🔧 Любая сложность" }, $"{prefix}:f:d"));

    public static readonly int[] MinutePresets = { 10, 15, 20, 30, 45, 60, 90 };

    public static BotReply TimeScreen(string question, string prefix, string back)
    {
        var rows = Rows(MinutePresets.Select(m => new InlineButton($"{m} мин", $"{prefix}:{m}")), 4);
        rows.Add(Row(new InlineButton("⬅️ Назад", back)));
        return new BotReply("⏱ " + question, rows);
    }

    public static BotReply DifficultyScreen(string question, string prefix, string back) => new(
        "🔧 " + question,
        new[]
        {
            Row(new InlineButton("🟢 Легко", $"{prefix}:1"), new InlineButton("🟡 Средне", $"{prefix}:2"), new InlineButton("🔴 Сложно", $"{prefix}:3")),
            Row(new InlineButton("⬅️ Назад", back))
        });

    public const int FoodPageSize = 12;

    public static int PageArg(string[] p, int index) =>
        p.Length > index && int.TryParse(p[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? Math.Max(0, v) : 0;

    public static BotReply FoodPage(
        string title, IReadOnlyList<Food> list, int page,
        Func<Food, int, string> pick, Func<int, string> nav, IReadOnlyList<InlineButton> footer,
        Func<Food, string>? label = null)
    {
        int pages = Math.Max(1, (list.Count + FoodPageSize - 1) / FoodPageSize);
        page = Math.Clamp(page, 0, pages - 1);

        var rows = Rows(list.Skip(page * FoodPageSize).Take(FoodPageSize)
            .Select(f => new InlineButton(label?.Invoke(f) ?? f.Name, pick(f, page))), 2);
        AddNav(rows, page, pages, nav);
        rows.Add(footer);
        return new BotReply(pages > 1 ? $"{title}\nСтраница {page + 1} из {pages}" : title, rows);
    }

    public static void AddNav(List<IReadOnlyList<InlineButton>> rows, int page, int pages, Func<int, string> nav)
    {
        if (pages <= 1) return;
        var row = new List<InlineButton>();
        if (page > 0) row.Add(new InlineButton("◀️", nav(page - 1)));
        row.Add(new InlineButton($"{page + 1}/{pages}", nav(page)));
        if (page < pages - 1) row.Add(new InlineButton("▶️", nav(page + 1)));
        rows.Add(row);
    }

    public static List<Food> MatchFoods(IReadOnlyList<Food> foods, string query)
    {
        var tokens = FoodMatcher.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return new List<Food>();

        var hits = foods
            .Where(f => { string n = FoodMatcher.Normalize(f.Name); return tokens.All(n.Contains); })
            .OrderBy(f => FoodMatcher.Normalize(f.Name).StartsWith(tokens[0]) ? 0 : 1)
            .ThenBy(f => f.Name)
            .ToList();

        if (hits.Count == 0)
        {
            var one = FoodMatcher.Find(query, foods);
            if (one != null) hits.Add(one);
        }
        return hits;
    }

    public static BotReply SearchReply(Session s, IReadOnlyList<Food> foods, int page)
    {
        char ctx = s.SearchCtx;
        string back = ctx switch { 'a' => "a:home", 'e' => "e:add", 'l' => "l:home", _ => "i:home" };
        string query = s.SearchQuery.Trim();
        var hits = MatchFoods(foods, query);

        if (hits.Count == 0)
        {
            var rows = new List<IReadOnlyList<InlineButton>>();
            if (ctx == 'a')
            {
                s.NewFoodName = query;
                rows.Add(Row(new InlineButton($"➕ Создать «{query}»", "a:new")));
            }
            rows.Add(Row(new InlineButton("🔎 Искать ещё", $"{ctx}:find"), new InlineButton("⬅️ Назад", back)));
            return new BotReply($"Не нашла «{query}». " + (ctx == 'a' ? "Можно добавить его самому." : "Попробуйте другое слово."), rows);
        }

        Func<Food, string>? mark = null;
        if (ctx == 'i') mark = f => (s.Selected.Contains(f.Id) ? "✅ " : "") + f.Name;
        return FoodPage($"🔎 «{query}» — найдено {hits.Count}", hits, page,
            (f, pg) => ctx switch { 'a' => $"a:p:{f.Id}", 'e' => $"e:p:{f.Id}", 'l' => $"l:p:{f.Id}", _ => $"i:s:{f.Id}:{pg}" },
            pg => $"{ctx}:q:{pg}",
            Row(new InlineButton("⬅️ Назад", back), new InlineButton("🔎 Искать ещё", $"{ctx}:find")),
            mark);
    }

    public static bool DishMatches(string query, Dish d)
    {
        var tokens = FoodMatcher.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return true;
        string hay = FoodMatcher.Normalize(d.Name + " " + string.Join(' ', d.Ingredients.Select(i => i.Food.Name)));
        return tokens.All(hay.Contains);
    }

    public const int DishPageSize = 8;

    public static readonly int[] GoalPresets = { 1500, 1800, 2000, 2200, 2500, 3000 };

    public static readonly double[] EatPortions = { 0.5, 1, 1.5, 2, 3 };

    public static string PortionText(double x) =>
        x == 0.5 ? "½" : x == 1.5 ? "1½" : x.ToString("0.#", CultureInfo.InvariantCulture);

    public static string Inv(double x) => x.ToString(CultureInfo.InvariantCulture);

    public static Nutrients Sum(IEnumerable<(int FoodId, double Grams)> items, IReadOnlyDictionary<int, Food> map) =>
        items.Aggregate(Nutrients.Zero, (acc, i) => acc + map[i.FoodId].NutrientsFor(i.Grams));

    public static string RecipeText(Dish dish) =>
        dish.Steps.Count == 0 ? string.Empty : "\n\n📝 Как готовить:\n" + InstructionText.Format(dish.Steps);

    public static (double? Qty, string Unit) ParseQuantity(string text)
    {
        var m = Regex.Match(text.Trim(), @"^(?<num>\d+(?:[.,]\d+)?)\s*(?<unit>[а-яa-z]*)\.?$", RegexOptions.IgnoreCase);
        if (!m.Success) return (null, string.Empty);
        double qty = double.Parse(m.Groups["num"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        return (qty, m.Groups["unit"].Value.ToLowerInvariant());
    }
}
