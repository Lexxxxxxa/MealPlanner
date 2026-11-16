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
/// What to cook flow: product selection, dish suggestions (strict and with shopping) and viewing results
/// </summary>
internal sealed class IdeaFeature : FeatureBase
{
    public IdeaFeature(BotContext ctx) : base(ctx)
    {
    }

    public override IReadOnlyCollection<string> Prefixes { get; } = new[] { "i", "r" };

    public override IReadOnlyCollection<Step> Steps { get; } = new[] { Step.IdeaText };

    public override Task<BotReply> HandleCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct) =>
        p[0] == "r" ? ResultCallbackAsync(userId, userName, s, p, ct) : IdeaCallbackAsync(userId, userName, s, p, ct);

    public override Task<BotReply> HandleStepAsync(long userId, string userName, Session s, string text, CancellationToken ct) =>
        IdeaFromTextAsync(userId, userName, s, text, ct);

    internal async Task<BotReply> IdeaHomeAsync(long userId, string userName, bool reset, CancellationToken ct)
    {
        var s = Ctx.Sessions.Get(userId);
        s.Step = Step.None;
        if (reset) { s.Selected.Clear(); s.Results.Clear(); s.IdeaBuy = false; }
        var foods = await FoodsAsync(ct);
        return IdeaHome(s, foods);
    }

    private static BotReply IdeaHome(Session s, IReadOnlyList<Food> foods)
    {
        var sb = new StringBuilder("🍳 Что у вас есть сейчас?\nОткрывайте категории и отмечайте продукты.");
        if (s.Selected.Count > 0)
        {
            var names = foods.Where(f => s.Selected.Contains(f.Id)).Select(f => f.Name).Take(15);
            sb.Append($"\n\nВыбрано ({s.Selected.Count}): {string.Join(", ", names)}");
        }

        var rows = Rows(Categories(foods).Select((c, i) => new InlineButton(CatLabel(c), $"i:c:{i}")), 2);
        rows.Add(Row(new InlineButton("🔎 Найти продукт", "i:find"), new InlineButton("✍️ Списком", "i:txt")));
        rows.Add(FilterRow(s));
        rows.Add(Row(new InlineButton("🗑 Сбросить", "i:reset"),
                     new InlineButton($"✅ Готово ({s.Selected.Count})", "i:done")));
        return new BotReply(sb.ToString(), rows);
    }

    private static BotReply IdeaCategory(Session s, IReadOnlyList<Food> foods, int ci, int page = 0)
    {
        var cats = Categories(foods);
        if (ci < 0 || ci >= cats.Count) return IdeaHome(s, foods);
        var list = foods.Where(f => f.Category == cats[ci]).OrderBy(f => f.Name).ToList();
        return FoodPage($"{CatLabel(cats[ci])} ({list.Count})\nНажмите на то, что есть:", list, page,
            (f, pg) => $"i:t:{f.Id}:{ci}:{pg}", pg => $"i:c:{ci}:{pg}",
            Row(new InlineButton("⬅️ Назад", "i:home"), new InlineButton("🔎 Найти", "i:find"),
                new InlineButton($"✅ Готово ({s.Selected.Count})", "i:done")),
            f => (s.Selected.Contains(f.Id) ? "✅ " : "") + f.Name);
    }

    private async Task<BotReply> IdeaCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct)
    {
        var foods = await FoodsAsync(ct);
        switch (p.Length > 1 ? p[1] : "")
        {
            case "c": return IdeaCategory(s, foods, int.Parse(p[2], CultureInfo.InvariantCulture), PageArg(p, 3));
            case "t":
                int id = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (!s.Selected.Add(id)) s.Selected.Remove(id);
                return IdeaCategory(s, foods, int.Parse(p[3], CultureInfo.InvariantCulture), PageArg(p, 4));
            case "find":
                s.SearchCtx = 'i';
                s.Step = Step.SearchFood;
                return new BotReply("Напишите название продукта или его часть, например: сыр");
            case "s":
            {
                int sid = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (!s.Selected.Add(sid)) s.Selected.Remove(sid);
                return SearchReply(s, foods, PageArg(p, 3));
            }
            case "q": return SearchReply(s, foods, PageArg(p, 2));
            case "reset": s.Selected.Clear(); s.IdeaBuy = false; return IdeaHome(s, foods);
            case "m":
                s.IdeaBuy = !s.IdeaBuy;
                return s.Selected.Count == 0 ? IdeaHome(s, foods) : await IdeaResultsAsync(userId, userName, s, foods, null, ct);
            case "home": return IdeaHome(s, foods);
            case "f":
                if (p[2] == "t") s.QuickOnly = !s.QuickOnly;
                else s.MaxDifficulty = s.MaxDifficulty <= 1 ? 3 : s.MaxDifficulty - 1;
                return s.Selected.Count == 0 ? IdeaHome(s, foods) : await IdeaResultsAsync(userId, userName, s, foods, null, ct);
            case "txt":
                s.Step = Step.IdeaText;
                return new BotReply("Напишите продукты через запятую, например:\nкурица, рис, лук, помидоры");
            case "done": return await IdeaResultsAsync(userId, userName, s, foods, null, ct);
            default: return IdeaHome(s, foods);
        }
    }

    private async Task<BotReply> IdeaFromTextAsync(long userId, string userName, Session s, string text, CancellationToken ct)
    {
        s.Step = Step.None;
        var foods = await FoodsAsync(ct);
        var unknown = new List<string>();
        foreach (var part in Regex.Split(text, @"[,;\n]"))
        {
            string name = part.Trim();
            if (name.Length == 0) continue;
            var food = FoodMatcher.Find(name, foods);
            if (food is null) unknown.Add(name); else s.Selected.Add(food.Id);
        }
        return await IdeaResultsAsync(userId, userName, s, foods, unknown, ct);
    }

    private async Task<BotReply> IdeaResultsAsync(
        long userId, string userName, Session s, IReadOnlyList<Food> foods, List<string>? unknown, CancellationToken ct)
    {
        if (s.Selected.Count == 0)
        {
            var home = IdeaHome(s, foods);
            return home with { Text = "Пока ничего не выбрано 🙂\n\n" + home.Text };
        }

        var member = await MemberAsync(userId, userName, ct);
        var own = (await Services.Dishes.ListAsync(member.HouseholdId, foods, ct)).Where(o => PassesFilter(s, o.Model)).ToList();
        var pantry = s.Selected.ToDictionary(id => id, _ => 100000.0);
        var rec = new SmartRecommender();
        var ctx = new RecommendationContext();

        var ownTop = PickIdeas(own.Select(o => o.Model).ToList(), s, pantry, ctx, rec, s.IdeaBuy ? 3 : 4);

        var ownNames = own.Select(o => o.Model.Name.ToLowerInvariant()).ToHashSet();
        var templates = TemplateDishes.Build(foods).Where(t => !ownNames.Contains(t.Name.ToLowerInvariant()) && PassesFilter(s, t)).ToList();
        var tmplTop = PickIdeas(templates, s, pantry, ctx, rec, s.IdeaBuy ? 6 : 5);

        s.Results.Clear();
        var sb = new StringBuilder(s.IdeaBuy
            ? "🛒 Блюда с вашими продуктами (недостающее придётся докупить):\n"
            : "🍽 Вот что можно приготовить из ваших продуктов:\n");
        if (s.QuickOnly || s.MaxDifficulty < 3) sb.AppendLine("Фильтры: " + FilterText(s));
        if (unknown is { Count: > 0 }) sb.AppendLine("(не нашла: " + string.Join(", ", unknown) + ")");
        sb.AppendLine();

        int n = 0;
        if (ownTop.Count > 0)
        {
            sb.AppendLine("Из ваших блюд:");
            foreach (var r in ownTop)
            {
                n++;
                s.Results.Add(new ResultItem(r.Dish, false, own.First(o => o.Model == r.Dish).Entity.Id));
                sb.AppendLine(Line(n, r, "📖"));
            }
            sb.AppendLine();
        }
        if (tmplTop.Count > 0)
        {
            sb.AppendLine("Идеи на сегодня:");
            foreach (var r in tmplTop)
            {
                n++;
                s.Results.Add(new ResultItem(r.Dish, true, null));
                sb.AppendLine(Line(n, r, "💡"));
            }
        }

        if (n == 0 && !s.IdeaBuy)
        {
            s.IdeaBuy = true;
            var buy = await IdeaResultsAsync(userId, userName, s, foods, unknown, ct);
            return buy with { Text = "Только из выбранных продуктов блюдо не собрать. Вот блюда, где они используются, часть ингредиентов нужно докупить 🛒\n\n" + buy.Text };
        }

        if (n == 0)
        {
            s.IdeaBuy = false;
            var home = IdeaHome(s, foods);
            return home with { Text = "Из этих продуктов ничего подходящего не нашлось 😔 Добавьте ещё продуктов.\n\n" + home.Text };
        }

        sb.AppendLine("\nНажмите номер, чтобы посмотреть состав.");
        var rows = new List<IReadOnlyList<InlineButton>>();
        rows.AddRange(Rows(Enumerable.Range(1, n).Select(i => new InlineButton(i.ToString(), $"r:v:{i - 1}")), 5));
        rows.Add(FilterRow(s));
        rows.Add(Row(new InlineButton(s.IdeaBuy ? "✅ Только из моих продуктов" : "🛒 С докупкой", "i:m")));
        rows.Add(Row(new InlineButton("🔄 Другие продукты", "r:again")));
        return new BotReply(sb.ToString().TrimEnd(), rows);
    }

    private static List<DishRecommendation> PickIdeas(
        IReadOnlyList<Dish> dishes, Session s, IReadOnlyDictionary<int, double> pantry,
        RecommendationContext ctx, SmartRecommender rec, int top)
    {
        if (!s.IdeaBuy)
            return rec.Recommend(dishes, pantry, ctx, top).Where(r => r.Coverage.IngredientShare >= 0.5).ToList();

        return dishes
            .Select(d => (Dish: d, Used: d.Ingredients.Count(i => s.Selected.Contains(i.Food.Id)), Cover: CoverageInfo.Analyze(d, pantry)))
            .Where(x => x.Used > 0)
            .OrderByDescending(x => x.Used)
            .ThenBy(x => x.Cover.Missing.Count)
            .ThenBy(x => x.Cover.MissingCost)
            .ThenBy(x => x.Dish.Name, StringComparer.Ordinal)
            .Take(top)
            .Select(x => new DishRecommendation(x.Dish, x.Used, x.Cover))
            .ToList();
    }

    private static string Line(int n, DishRecommendation r, string icon)
    {
        string head = $"{n}. {icon} {r.Dish.Name} — {r.Dish.PerServing.Kcal:F0} ккал/порция · ⏱ {r.Dish.CookMinutes} мин";
        if (r.Coverage.IsCookable) return head + "\n    ✅ всё есть";
        var miss = r.Coverage.Missing.Take(3).Select(m => m.Food.Name);
        string more = r.Coverage.Missing.Count > 3 ? $" и ещё {r.Coverage.Missing.Count - 3}" : "";
        return head + $"\n    не хватает: {string.Join(", ", miss)}{more}";
    }

    private async Task<BotReply> ResultCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct)
    {
        var foods = await FoodsAsync(ct);
        switch (p.Length > 1 ? p[1] : "")
        {
            case "again": s.IdeaBuy = false; return IdeaHome(s, foods);
            case "back": return await IdeaResultsAsync(userId, userName, s, foods, null, ct);
            case "v":
            {
                int i = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (i < 0 || i >= s.Results.Count) return await IdeaResultsAsync(userId, userName, s, foods, null, ct);
                var item = s.Results[i];
                var d = item.Dish;

                var sb = new StringBuilder($"{(item.IsTemplate ? "💡" : "📖")} {d.Name} ({d.Servings} порц.)\n{TimeInfo(d)}\n");
                sb.AppendLine("На порцию: " + Nutr(d.PerServing));
                sb.AppendLine("\nСостав:");
                foreach (var ing in d.Ingredients)
                    sb.AppendLine($"{(s.Selected.Contains(ing.Food.Id) ? "✅" : "❌")} {ing.Food.Name} — {ing.Grams:F0} г");

                var rows = new List<IReadOnlyList<InlineButton>>();
                if (item.IsTemplate) rows.Add(Row(new InlineButton("💾 Сохранить в мои блюда", $"r:save:{i}")));
                rows.Add(Row(new InlineButton("⬅️ К списку", "r:back")));
                return new BotReply(sb.ToString().TrimEnd(), rows);
            }
            case "save":
            {
                int i = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (i < 0 || i >= s.Results.Count) return await IdeaResultsAsync(userId, userName, s, foods, null, ct);
                var member = await MemberAsync(userId, userName, ct);
                var d = s.Results[i].Dish;
                await Services.Dishes.SaveAsync(member.HouseholdId, d, ct);
                return new BotReply($"✅ «{d.Name}» сохранено в ваши блюда.",
                    new[] { Row(new InlineButton("⬅️ К списку", "r:back")) });
            }
            default: return await IdeaResultsAsync(userId, userName, s, foods, null, ct);
        }
    }
}
