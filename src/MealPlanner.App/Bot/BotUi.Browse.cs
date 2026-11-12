using System.Globalization;
using MealPlanner.Core.Models;
using MealPlanner.Core.Parsing;

namespace MealPlanner.App.Bot;

public partial class BotUi
{
    private const int FoodPageSize = 12;

    private static int PageArg(string[] p, int index) =>
        p.Length > index && int.TryParse(p[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? Math.Max(0, v) : 0;

    private static BotReply FoodPage(
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

    private static void AddNav(List<IReadOnlyList<InlineButton>> rows, int page, int pages, Func<int, string> nav)
    {
        if (pages <= 1) return;
        var row = new List<InlineButton>();
        if (page > 0) row.Add(new InlineButton("◀️", nav(page - 1)));
        row.Add(new InlineButton($"{page + 1}/{pages}", nav(page)));
        if (page < pages - 1) row.Add(new InlineButton("▶️", nav(page + 1)));
        rows.Add(row);
    }

    private static List<Food> MatchFoods(IReadOnlyList<Food> foods, string query)
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

    private static BotReply SearchReply(Session s, IReadOnlyList<Food> foods, int page)
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

    private static bool DishMatches(string query, Dish d)
    {
        var tokens = FoodMatcher.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return true;
        string hay = FoodMatcher.Normalize(d.Name + " " + string.Join(' ', d.Ingredients.Select(i => i.Food.Name)));
        return tokens.All(hay.Contains);
    }
}
