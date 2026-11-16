using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MealPlanner.App.Data;
using MealPlanner.App.Services;
using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Parsing;
using MealPlanner.Core.Recommendation;
using static MealPlanner.App.Bot.UiKit;

namespace MealPlanner.App.Bot;

/// <summary>
/// Built-in dish database: categories, search, saving to own dishes and logging to the diary
/// </summary>
internal sealed class CatalogFeature : FeatureBase
{
    public CatalogFeature(BotContext ctx) : base(ctx)
    {
    }

    public override IReadOnlyCollection<string> Prefixes { get; } = new[] { "b" };

    public override IReadOnlyCollection<Step> Steps { get; } = new[] { Step.DishSearch };

    public override Task<BotReply> HandleCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct) =>
        CatalogCallbackAsync(userId, userName, s, p, ct);

    public override Task<BotReply> HandleStepAsync(long userId, string userName, Session s, string text, CancellationToken ct)
    {
        s.Step = Step.None;
        s.CatQuery = text;
        return CatalogSearchAsync(s, 0, ct);
    }

    internal Task<BotReply> OpenAsync(Session s, CancellationToken ct)
    {
        s.Step = Step.None;
        return CatalogHomeAsync(s, ct);
    }

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
                var member = await MemberAsync(userId, userName, ct);
                await Services.Dishes.SaveAsync(member.HouseholdId, d, ct);
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
                var member = await MemberAsync(userId, userName, ct);
                var d = all[n].Dish;
                await Services.Diary.AddAsync(member.Id, Ctx.Today(), $"{d.Name} × {PortionText(portions)}", d.PerServing * portions, ct);
                s.DiaryOffset = 0;
                return await Ctx.Features.Diary.DayAsync(userId, userName, s, ct);
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
        sb.Append(RecipeText(d));
        return new BotReply(sb.ToString().TrimEnd(), new[]
        {
            Row(new InlineButton("💾 В мои блюда", $"b:s:{index}"), new InlineButton("🍽 Съел(а)", $"b:e:{index}")),
            Row(new InlineButton("⬅️ Назад", s.CatBack))
        });
    }
}
