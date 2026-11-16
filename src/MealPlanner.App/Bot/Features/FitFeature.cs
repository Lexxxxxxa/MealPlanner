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
/// Dish suggestions that close the remaining calories and macros of the day
/// </summary>
internal sealed class FitFeature : FeatureBase
{
    public FitFeature(BotContext ctx) : base(ctx)
    {
    }

    private static readonly double[] FitPortions = { 0.5, 1, 1.5, 2 };

    private async Task<(Nutrients Eaten, Nutrients Remaining, double Target)> RemainingAsync(Member member, DateOnly date, CancellationToken ct)
    {
        var day = await Services.Diary.SummaryAsync(member.Id, date, ct);
        return (day.Eaten, day.Remaining, day.TargetKcal);
    }

    internal async Task<BotReply> FitCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct)
    {
        var member = await MemberAsync(userId, userName, ct);
        var (_, remaining, _) = await RemainingAsync(member, Ctx.Today(), ct);

        switch (p[1])
        {
            case "fv":
            {
                int i = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (i < 0 || i >= s.Fit.Count) return await FitListAsync(member, s, ct);
                var item = s.Fit[i];
                var portion = item.Dish.PerServing * item.Portions;
                var after = new Nutrients(remaining.Kcal - portion.Kcal, remaining.Protein - portion.Protein, remaining.Fat - portion.Fat, remaining.Carbs - portion.Carbs);
                var sb = new StringBuilder($"{(item.IsTemplate ? "💡" : "📖")} {item.Dish.Name} × {PortionText(item.Portions)}\n{TimeInfo(item.Dish)}\n\n");
                sb.AppendLine("Порция: " + Nutr(portion));
                sb.AppendLine("\nСостав:");
                foreach (var ing in item.Dish.Ingredients)
                    sb.AppendLine($"• {ing.Food.Name} — {ing.Grams * item.Portions / Math.Max(1, item.Dish.Servings):F0} г");
                sb.Append(RecipeText(item.Dish));
                sb.Append($"\n\nПосле этого останется: {Math.Max(0, after.Kcal):F0} ккал · Б {Math.Max(0, after.Protein):F0} / Ж {Math.Max(0, after.Fat):F0} / У {Math.Max(0, after.Carbs):F0}");
                return new BotReply(sb.ToString(), new[]
                {
                    Row(new InlineButton("🍽 Записать в дневник", $"t:fa:{i}")),
                    Row(new InlineButton("⬅️ К списку", "t:fit"))
                });
            }
            case "fa":
            {
                int i = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (i < 0 || i >= s.Fit.Count) return await FitListAsync(member, s, ct);
                var item = s.Fit[i];
                await Services.Diary.AddAsync(member.Id, Ctx.Today(), $"{item.Dish.Name} × {PortionText(item.Portions)}", item.Dish.PerServing * item.Portions, ct);
                s.DiaryOffset = 0;
                return await Ctx.Features.Diary.DayAsync(userId, userName, s, ct);
            }
            default:
                return await FitListAsync(member, s, ct);
        }
    }

    private async Task<BotReply> FitListAsync(Member member, Session s, CancellationToken ct)
    {
        var (_, rem, _) = await RemainingAsync(member, Ctx.Today(), ct);
        var back = new[] { Row(new InlineButton("⬅️ К дневнику", "t:day")) };

        if (rem.Kcal < 80)
            return new BotReply(rem.Kcal >= -80
                ? "✅ Норма на сегодня выполнена, больше есть не нужно."
                : $"Норма на сегодня уже превышена на {-rem.Kcal:F0} ккал.", back);

        var foods = await FoodsAsync(ct);
        var own = await Services.Dishes.ListAsync(member.HouseholdId, foods, ct);
        var ownNames = own.Select(o => o.Model.Name.ToLowerInvariant()).ToHashSet();
        var candidates = own.Select(o => (Dish: o.Model, IsTemplate: false))
            .Concat(TemplateDishes.Build(foods).Where(t => !ownNames.Contains(t.Name.ToLowerInvariant())).Select(t => (Dish: t, IsTemplate: true)))
            .ToList();

        var target = PortionFit.Clip(rem);
        s.Fit.Clear();
        var scored = new List<(FitItem Item, double Score)>();
        foreach (var (dish, isTemplate) in candidates)
        {
            var options = FitPortions
                .Select(m => (Portions: m, Value: dish.PerServing * m))
                .Where(x => x.Value.Kcal <= rem.Kcal * 1.1)
                .Select(x => (x.Portions, Score: PortionFit.Score(x.Value, target)))
                .OrderBy(x => x.Score)
                .ToList();
            if (options.Count > 0) scored.Add((new FitItem(dish, options[0].Portions, isTemplate), options[0].Score));
        }

        var top = scored.OrderBy(x => x.Score).Take(5).Select(x => x.Item).ToList();
        string header = $"💡 Осталось до нормы: {rem.Kcal:F0} ккал · Б {target.Protein:F0} / Ж {target.Fat:F0} / У {target.Carbs:F0} г\n";
        if (top.Count == 0)
            return new BotReply(header + "\nПод такой остаток нет подходящего блюда. Запишите продукт или небольшой перекус.",
                new[] { Row(new InlineButton("➕ Продукт", "l:home")), back[0] });

        s.Fit.AddRange(top);
        var sb = new StringBuilder(header + "\nЭти блюда лучше всего закроют остаток:\n\n");
        for (int i = 0; i < top.Count; i++)
        {
            var n = top[i].Dish.PerServing * top[i].Portions;
            sb.AppendLine($"{i + 1}. {(top[i].IsTemplate ? "💡" : "📖")} {top[i].Dish.Name} × {PortionText(top[i].Portions)} — {n.Kcal:F0} ккал");
            sb.AppendLine($"    Б {n.Protein:F0} / Ж {n.Fat:F0} / У {n.Carbs:F0} · ⏱ {top[i].Dish.CookMinutes} мин");
        }
        sb.Append("\nНажмите номер, чтобы посмотреть состав.");
        var rows = new List<IReadOnlyList<InlineButton>>
        {
            Row(Enumerable.Range(1, top.Count).Select(i => new InlineButton(i.ToString(), $"t:fv:{i - 1}")).ToArray()),
            back[0]
        };
        return new BotReply(sb.ToString(), rows);
    }
}
