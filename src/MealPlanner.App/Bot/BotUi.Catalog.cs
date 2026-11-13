using System.Globalization;
using System.Text;
using MealPlanner.App.Data;
using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.App.Bot;

public partial class BotUi
{
    private const int DishPageSize = 8;

    private async Task<List<(int Index, CatalogDish Item)>> CatalogAsync(Session s, CancellationToken ct)
    {
        var foods = await FoodsAsync(ct);
        return DishCatalog.Build(foods).Select((c, i) => (i, c)).Where(x => PassesFilter(s, x.c.Dish)).ToList();
    }

    private static InlineButton DishButton(int index, Dish d) =>
        new($"{d.Name} · {d.PerServing.Kcal:F0} ккал · ⏱{d.CookMinutes}", $"b:v:{index}");

    private async Task<BotReply> CatalogHomeAsync(Session s, CancellationToken ct)
    {
        s.CatBack = "b:home";
        var items = await CatalogAsync(s, ct);
        var cats = DishCatalog.Categories;
        var rows = Rows(cats.Select((c, i) => new InlineButton($"{CatLabel(c)} ({items.Count(x => x.Item.Category == c)})", $"b:c:{i}:0")), 2);
        rows.Add(FilterRow(s, "b"));
        rows.Add(Row(new InlineButton("🔎 Найти блюдо", "b:find"), new InlineButton("🎲 Случайное", "b:rnd")));
        string filters = s.QuickOnly || s.MaxDifficulty < 3 ? $"\nФильтры: {FilterText(s)}" : "";
        return new BotReply($"📚 База блюд: {items.Count} блюд{filters}\nВыберите категорию или найдите блюдо по названию или продукту.", rows);
    }

    private async Task<BotReply> CatalogCategoryAsync(Session s, int ci, int page, CancellationToken ct)
    {
        var cats = DishCatalog.Categories;
        if (ci < 0 || ci >= cats.Count) return await CatalogHomeAsync(s, ct);
        var items = (await CatalogAsync(s, ct)).Where(x => x.Item.Category == cats[ci]).ToList();
        s.CatBack = $"b:c:{ci}:{page}";
        return DishPage($"{CatLabel(cats[ci])} ({items.Count})", items, page, pg => $"b:c:{ci}:{pg}",
            new[] { FilterRow(s, "b"), Row(new InlineButton("⬅️ К категориям", "b:home"), new InlineButton("🔎 Найти", "b:find")) });
    }

    private async Task<BotReply> CatalogSearchAsync(Session s, int page, CancellationToken ct)
    {
        var items = (await CatalogAsync(s, ct)).Where(x => DishMatches(s.CatQuery, x.Item.Dish)).ToList();
        s.CatBack = $"b:q:{page}";
        var footer = new[] { Row(new InlineButton("⬅️ К категориям", "b:home"), new InlineButton("🔎 Искать ещё", "b:find")) };
        if (items.Count == 0)
            return new BotReply($"Ничего не нашла по запросу «{s.CatQuery.Trim()}». Попробуйте другое слово или уберите фильтры.", footer);
        return DishPage($"🔎 «{s.CatQuery.Trim()}» — найдено {items.Count}", items, page, pg => $"b:q:{pg}", footer);
    }

    private static BotReply DishPage(
        string title, List<(int Index, CatalogDish Item)> items, int page, Func<int, string> nav,
        IReadOnlyList<IReadOnlyList<InlineButton>> footer)
    {
        int pages = Math.Max(1, (items.Count + DishPageSize - 1) / DishPageSize);
        page = Math.Clamp(page, 0, pages - 1);
        var rows = Rows(items.Skip(page * DishPageSize).Take(DishPageSize).Select(x => DishButton(x.Index, x.Item.Dish)), 1);
        AddNav(rows, page, pages, nav);
        rows.AddRange(footer);
        return new BotReply(pages > 1 ? $"{title}\nСтраница {page + 1} из {pages}" : title, rows);
    }

    private async Task<BotReply> CatalogCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct)
    {
        string action = p.Length > 1 ? p[1] : "";
        switch (action)
        {
            case "c": return await CatalogCategoryAsync(s, int.Parse(p[2], CultureInfo.InvariantCulture), PageArg(p, 3), ct);
            case "q": return await CatalogSearchAsync(s, PageArg(p, 2), ct);
            case "find":
                s.Step = Step.DishSearch;
                return new BotReply("Напишите название блюда или продукт, например: «борщ», «курица» или «творог яйца»");
            case "f":
            {
                if (p[2] == "t") s.QuickOnly = !s.QuickOnly;
                else s.MaxDifficulty = s.MaxDifficulty <= 1 ? 3 : s.MaxDifficulty - 1;
                var back = s.CatBack.Split(':');
                return back[0] == "b" && back.Length > 2 && back[1] == "c"
                    ? await CatalogCategoryAsync(s, int.Parse(back[2], CultureInfo.InvariantCulture), 0, ct)
                    : await CatalogHomeAsync(s, ct);
            }
            case "rnd":
            {
                var items = await CatalogAsync(s, ct);
                if (items.Count == 0) return await CatalogHomeAsync(s, ct);
                return await CatalogViewAsync(s, items[Random.Shared.Next(items.Count)].Index, ct);
            }
            case "v": return await CatalogViewAsync(s, int.Parse(p[2], CultureInfo.InvariantCulture), ct);
            case "s":
            {
                var all = DishCatalog.Build(await FoodsAsync(ct));
                int n = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (n < 0 || n >= all.Count) return await CatalogHomeAsync(s, ct);
                var d = all[n].Dish;
                var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
                await SaveDishAsync(member, d.Name, d.Servings, d.Ingredients.Select(x => (x.Food.Id, x.Grams)).ToList(), ct, d.CookMinutes, d.Difficulty);
                return new BotReply($"✅ «{d.Name}» сохранено в «Мои блюда». Там его можно изменить.",
                    new[] { Row(new InlineButton("⬅️ Назад", $"b:v:{n}"), new InlineButton("📖 Мои блюда", "d:list")) });
            }
            case "e":
            {
                var all = DishCatalog.Build(await FoodsAsync(ct));
                int n = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (n < 0 || n >= all.Count) return await CatalogHomeAsync(s, ct);
                var d = all[n].Dish;
                return new BotReply($"Сколько порций «{d.Name}» вы съели?\nНа порцию: {Nutr(d.PerServing)}",
                    Rows(EatPortions.Select(x => new InlineButton(PortionText(x), $"b:ate:{n}:{Inv(x)}")), 3)
                        .Append(Row(new InlineButton("⬅️ Назад", $"b:v:{n}"))).ToList());
            }
            case "ate":
            {
                var all = DishCatalog.Build(await FoodsAsync(ct));
                int n = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (n < 0 || n >= all.Count) return await CatalogHomeAsync(s, ct);
                double portions = double.Parse(p[3], CultureInfo.InvariantCulture);
                var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
                var d = all[n].Dish;
                await AddDiaryAsync(member, s, $"{d.Name} × {PortionText(portions)}", d.PerServing * portions, ct);
                s.DiaryOffset = 0;
                return await DayAsync(userId, userName, s, ct);
            }
            default: return await CatalogHomeAsync(s, ct);
        }
    }

    private async Task<BotReply> CatalogViewAsync(Session s, int index, CancellationToken ct)
    {
        var all = DishCatalog.Build(await FoodsAsync(ct));
        if (index < 0 || index >= all.Count) return await CatalogHomeAsync(s, ct);
        var (category, d) = (all[index].Category, all[index].Dish);

        var sb = new StringBuilder($"📚 {d.Name}\n{category} · {d.Servings} порц.\n{TimeInfo(d)}\n\n");
        sb.AppendLine("На порцию: " + Nutr(d.PerServing));
        sb.AppendLine("\nСостав:");
        foreach (var ing in d.Ingredients) sb.AppendLine($"• {ing.Food.Name} — {ing.Grams:F0} г");
        return new BotReply(sb.ToString().TrimEnd(), new[]
        {
            Row(new InlineButton("💾 В мои блюда", $"b:s:{index}"), new InlineButton("🍽 Съел(а)", $"b:e:{index}")),
            Row(new InlineButton("⬅️ Назад", s.CatBack))
        });
    }

    private async Task<BotReply> DishListAsync(long userId, string userName, CancellationToken ct, int page = 0)
    {
        var s = _sessions.Get(userId);
        var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
        var foods = await FoodsAsync(ct);
        var dishes = await _legacy.LoadDishesAsync(member.HouseholdId, foods, ct);

        if (dishes.Count == 0)
            return new BotReply("📖 В вашей базе пока нет блюд.\nНажмите «➕ Добавить блюдо» внизу, сохраните блюдо из «📚 База блюд» или посмотрите идеи в «🍳 Что приготовить».");

        var shown = dishes.Where(d => DishMatches(s.OwnQuery, d.Model)).ToList();
        int pages = Math.Max(1, (shown.Count + DishPageSize - 1) / DishPageSize);
        page = Math.Clamp(page, 0, pages - 1);

        var rows = Rows(shown.Skip(page * DishPageSize).Take(DishPageSize).Select(d => new InlineButton(
            $"{d.Model.Name} · {d.Model.PerServing.Kcal:F0} ккал", $"d:v:{d.Entity.Id}")), 1);
        AddNav(rows, page, pages, pg => $"d:list:{pg}");
        var footer = new List<InlineButton> { new("🔎 Найти", "d:find") };
        if (s.OwnQuery.Length > 0) footer.Add(new InlineButton("✖️ Сбросить поиск", "d:clr"));
        rows.Add(footer);

        string head = s.OwnQuery.Length > 0
            ? $"📖 «{s.OwnQuery.Trim()}» — найдено {shown.Count} из {dishes.Count}"
            : $"📖 Ваши блюда ({dishes.Count}):\nНажмите на блюдо, чтобы увидеть состав.";
        if (shown.Count == 0) head += "\nНичего не нашла, попробуйте другое слово.";
        return new BotReply(pages > 1 ? $"{head}\nСтраница {page + 1} из {pages}" : head, rows);
    }
}
