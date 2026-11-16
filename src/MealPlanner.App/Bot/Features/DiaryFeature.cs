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
/// Food diary: day summary, logging products and dishes, manual daily norm
/// </summary>
internal sealed class DiaryFeature : FeatureBase
{
    public DiaryFeature(BotContext ctx) : base(ctx)
    {
    }

    public override IReadOnlyCollection<string> Prefixes { get; } = new[] { "t", "l" };

    public override IReadOnlyCollection<Step> Steps { get; } = new[] { Step.LogGrams, Step.GoalText };

    public override Task<BotReply> HandleCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct) =>
        p[0] == "l" ? LogCallbackAsync(userId, userName, s, p, ct) : DiaryCallbackAsync(userId, userName, s, p, ct);

    public override async Task<BotReply> HandleStepAsync(long userId, string userName, Session s, string text, CancellationToken ct)
    {
        var (qty, unit) = ParseQuantity(text);
        if (s.Step == Step.GoalText) return await SetGoalFromTextAsync(userId, userName, s, qty, ct);

        var food = (await FoodsAsync(ct)).FirstOrDefault(f => f.Id == s.LogFoodId);
        if (food is null) { s.Step = Step.None; return new BotReply("Что-то пошло не так. Выберите действие в меню внизу."); }

        double? grams = qty is null ? null : DishParser.ToGrams(qty.Value, unit, food);
        if (grams is null || grams <= 0 || grams > 10000)
            return new BotReply("Не поняла количество. Напишите, например: 120, 0,5 кг или 3 шт");

        s.Step = Step.None;
        var member = await MemberAsync(userId, userName, ct);
        await Services.Diary.AddAsync(member.Id, Ctx.Today(), $"{food.Name} {grams.Value:F0} г", food.NutrientsFor(grams.Value), ct);
        s.DiaryOffset = 0;
        return await DayAsync(userId, userName, s, ct);
    }

    private async Task<BotReply> SetGoalFromTextAsync(long userId, string userName, Session s, double? kcal, CancellationToken ct)
    {
        if (kcal is null or < 800 or > 6000)
            return new BotReply("Напишите число калорий в день от 800 до 6000, например: 2100");

        s.Step = Step.None;
        var member = await MemberAsync(userId, userName, ct);
        await Services.Diary.SetManualTargetAsync(member.Id, kcal.Value, ct);
        return await DayAsync(userId, userName, s, ct);
    }

    internal Task<BotReply> OpenAsync(long userId, string userName, Session s, CancellationToken ct)
    {
        s.Step = Step.None;
        s.DiaryOffset = 0;
        return DayAsync(userId, userName, s, ct);
    }

    private static string Bar(double value, double target)
    {
        double share = target <= 0 ? 0 : value / target;
        int filled = (int)Math.Round(Math.Min(1, share) * 10);
        return new string('█', filled) + new string('░', 10 - filled) + $" {share * 100:F0}%";
    }

    private string DayTitle(int offset)
    {
        var d = Ctx.Today().AddDays(offset);
        string date = d.ToString("dd.MM", CultureInfo.InvariantCulture);
        return offset == 0 ? $"Сегодня, {date}" : offset == -1 ? $"Вчера, {date}" : d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }

    internal async Task<BotReply> DayAsync(long userId, string userName, Session s, CancellationToken ct)
    {
        var member = await MemberAsync(userId, userName, ct);
        var date = Ctx.Today().AddDays(s.DiaryOffset);
        var day = await Services.Diary.SummaryAsync(member.Id, date, ct);
        var entries = day.Entries;
        var prof = day.Profile;
        double target = day.TargetKcal;
        double kcal = day.Eaten.Kcal, prot = day.Eaten.Protein, fat = day.Eaten.Fat, carb = day.Eaten.Carbs;

        var sb = new StringBuilder($"📅 {DayTitle(s.DiaryOffset)}\n\n");
        if (entries.Count == 0) sb.AppendLine("Пока ничего не записано.");
        for (int i = 0; i < entries.Count; i++) sb.AppendLine($"{i + 1}. {entries[i].Label} — {entries[i].Kcal:F0} ккал");

        sb.AppendLine($"\n🔥 Калории: {kcal:F0} из {target:F0}");
        if (prof is { HasProfile: true }) sb.AppendLine($"🎯 {EnergyCalculator.GoalLabel(prof.Goal)}");
        sb.AppendLine(Bar(kcal, target));
        sb.AppendLine(kcal <= target ? $"Осталось: {target - kcal:F0} ккал" : $"Перебор: +{kcal - target:F0} ккал");
        sb.AppendLine($"\nБелки: {prot:F0} из {day.Goal.Protein:F0} г");
        sb.AppendLine($"Жиры: {fat:F0} из {day.Goal.Fat:F0} г");
        sb.Append($"Углеводы: {carb:F0} из {day.Goal.Carbs:F0} г");

        var rows = new List<IReadOnlyList<InlineButton>>
        {
            Row(new InlineButton("➕ Блюдо", "t:dish"), new InlineButton("➕ Продукт", "l:home"))
        };
        if (entries.Count > 0)
            rows.AddRange(Rows(entries.Select((e, i) => new InlineButton($"🗑 {i + 1}", $"t:del:{e.Id}")), 4));
        var nav = new List<InlineButton> { new("◀️", "t:prev") };
        if (s.DiaryOffset < 0) nav.Add(new("Сегодня", "t:today"));
        if (s.DiaryOffset < 0) nav.Add(new("▶️", "t:next"));
        rows.Add(nav);
        if (s.DiaryOffset == 0 && kcal < target - 80)
            rows.Add(Row(new InlineButton("💡 Что съесть до нормы", "t:fit")));
        rows.Add(Row(new InlineButton("🎯 Моя цель", "t:goal")));
        return new BotReply(sb.ToString(), rows);
    }

    private async Task<BotReply> DiaryCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct)
    {
        var member = await MemberAsync(userId, userName, ct);
        if (p.Length > 1 && p[1] is "fit" or "fv" or "fa")
            return await Ctx.Features.Fit.FitCallbackAsync(userId, userName, s, p, ct);
        switch (p.Length > 1 ? p[1] : "")
        {
            case "prev": s.DiaryOffset--; break;
            case "next": s.DiaryOffset = Math.Min(0, s.DiaryOffset + 1); break;
            case "today": s.DiaryOffset = 0; break;
            case "del":
            {
                int id = int.Parse(p[2], CultureInfo.InvariantCulture);
                await Services.Diary.DeleteAsync(member.Id, id, ct);
                break;
            }
            case "dish":
            {
                var foods = await FoodsAsync(ct);
                var dishes = await Services.Dishes.ListAsync(member.HouseholdId, foods, ct);
                if (dishes.Count == 0)
                    return new BotReply("У вас пока нет сохранённых блюд. Добавьте их в «➕ Добавить блюдо» или запишите продукт.",
                        new[] { Row(new InlineButton("➕ Продукт", "l:home"), new InlineButton("⬅️ Назад", "t:day")) });
                var rows = Rows(dishes.Select(d => new InlineButton($"{d.Model.Name} · {d.Model.PerServing.Kcal:F0} ккал", $"d:eat:{d.Entity.Id}")), 1);
                rows.Add(Row(new InlineButton("⬅️ Назад", "t:day")));
                return new BotReply("Что вы съели?", rows);
            }
            case "goal":
                return await Ctx.Features.Profile.GoalHomeAsync(member, ct);
            case "g":
                await Services.Diary.SetManualTargetAsync(member.Id, double.Parse(p[2], CultureInfo.InvariantCulture), ct);
                break;
            case "gx":
                s.Step = Step.GoalText;
                return new BotReply("Напишите число калорий в день, например: 2100");
        }
        return await DayAsync(userId, userName, s, ct);
    }

    private async Task<BotReply> LogCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct)
    {
        var foods = await FoodsAsync(ct);
        var map = foods.ToDictionary(f => f.Id);
        var cats = Categories(foods);
        switch (p.Length > 1 ? p[1] : "")
        {
            case "c":
            {
                int ci = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (ci < 0 || ci >= cats.Count) goto default;
                var list = foods.Where(f => f.Category == cats[ci]).OrderBy(f => f.Name).ToList();
                return FoodPage($"{CatLabel(cats[ci])} ({list.Count})\nЧто вы съели?", list, PageArg(p, 3),
                    (f, _) => $"l:p:{f.Id}", pg => $"l:c:{ci}:{pg}",
                    Row(new InlineButton("⬅️ Назад", "l:home"), new InlineButton("🔎 Найти", "l:find")));
            }
            case "find":
                s.SearchCtx = 'l';
                s.Step = Step.SearchFood;
                return new BotReply("Напишите название продукта или его часть, например: сыр");
            case "q": return SearchReply(s, foods, PageArg(p, 2));
            case "p":
            {
                int id = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (!map.TryGetValue(id, out var food)) goto default;
                s.LogFoodId = id;
                var rows = Rows(GramPresets.Select(g => new InlineButton($"{g} г", $"l:g:{id}:{g}")), 4);
                rows.Add(Row(new InlineButton("✍️ Другое число", $"l:gx:{id}"), new InlineButton("⬅️ Назад", "l:home")));
                return new BotReply($"Сколько «{food.Name}» вы съели?", rows);
            }
            case "g":
            {
                int id = int.Parse(p[2], CultureInfo.InvariantCulture);
                double g = double.Parse(p[3], CultureInfo.InvariantCulture);
                var member = await MemberAsync(userId, userName, ct);
                await Services.Diary.AddAsync(member.Id, Ctx.Today(), $"{map[id].Name} {g:F0} г", map[id].NutrientsFor(g), ct);
                s.DiaryOffset = 0;
                return await DayAsync(userId, userName, s, ct);
            }
            case "gx":
                s.LogFoodId = int.Parse(p[2], CultureInfo.InvariantCulture);
                s.Step = Step.LogGrams;
                return new BotReply("Напишите количество, например: 120, 0,5 кг или 3 шт");
            default:
            {
                var rows = Rows(cats.Select((c, i) => new InlineButton(CatLabel(c), $"l:c:{i}")), 2);
                rows.Add(Row(new InlineButton("🔎 Найти продукт", "l:find"), new InlineButton("⬅️ Назад", "t:day")));
                return new BotReply("Что вы съели? Выберите категорию:", rows);
            }
        }
    }
}
