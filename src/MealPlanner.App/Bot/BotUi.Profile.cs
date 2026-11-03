using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MealPlanner.App.Data;
using MealPlanner.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.App.Bot;

public partial class BotUi
{
    private const Sex DefSex = Sex.Male;
    private const int DefAge = 30;
    private const double DefHeight = 170, DefWeight = 70, DefActivity = 1.375;

    private async Task<MemberTarget?> ProfileAsync(int memberId, CancellationToken ct) =>
        await _db.MemberTargets.AsNoTracking().FirstOrDefaultAsync(t => t.MemberId == memberId, ct);

    private static double Estimate(ProfileDraft d, DietGoal? goalOverride = null, double? activityOverride = null) =>
        EnergyCalculator.DailyTarget(d.Sex ?? DefSex, d.Age ?? DefAge, d.Height ?? DefHeight, d.Weight ?? DefWeight,
            activityOverride ?? d.Activity ?? DefActivity, goalOverride ?? d.Goal ?? DietGoal.Maintain);

    private static string EstimateLine(ProfileDraft d)
    {
        bool complete = d.Sex != null && d.Age != null && d.Height != null && d.Weight != null && d.Activity != null && d.Goal != null;
        return complete
            ? $"🔥 Норма на день: {Estimate(d):F0} ккал"
            : $"🔥 Ориентир на день: ≈ {Estimate(d):F0} ккал (уточнится по мере ввода данных)";
    }

    private static string Steps(string title, ProfileDraft d, string question) =>
        $"{title}\n{EstimateLine(d)}\n\n{question}";

    private async Task<BotReply> GoalHomeAsync(Member member, CancellationToken ct)
    {
        var t = await ProfileAsync(member.Id, ct);
        var sb = new StringBuilder("🎯 Ваша норма калорий\n\n");
        if (t is null || !t.HasProfile)
        {
            double kcal = t?.Kcal ?? 2000;
            sb.AppendLine($"Сейчас: {kcal:F0} ккал в день{(t is { IsManual: true } ? " (задано вручную)" : " (стандартное значение)")}");
            sb.Append("\nЧтобы норма была точной, рассчитайте её по вашим данным: рост, вес, возраст, активность и цель.");
        }
        else
        {
            double bmi = EnergyCalculator.Bmi(t.WeightKg, t.HeightCm);
            sb.AppendLine($"Цель: {EnergyCalculator.GoalLabel(t.Goal)}");
            sb.AppendLine($"ИМТ: {bmi:F1} ({EnergyCalculator.BmiCategory(bmi)})");
            sb.AppendLine($"Норма: {t.Kcal:F0} ккал в день{(t.IsManual ? " (изменена вручную)" : "")}");
        }
        var rows = new List<IReadOnlyList<InlineButton>>
        {
            Row(new InlineButton("📝 Рассчитать по моим данным", "g:start")),
            Row(new InlineButton("✍️ Задать вручную", "g:man")),
            Row(new InlineButton("⬅️ Назад", "t:day"))
        };
        return new BotReply(sb.ToString().TrimEnd(), rows);
    }

    private static BotReply ManualGoalScreen(double cur)
    {
        var rows = Rows(GoalPresets.Select(g => new InlineButton($"{g}", $"t:g:{g}")), 3);
        rows.Add(Row(new InlineButton("✍️ Другое число", "t:gx"), new InlineButton("⬅️ Назад", "t:goal")));
        return new BotReply($"Сколько калорий в день вам нужно?\nСейчас: {cur:F0} ккал.", rows);
    }

    private async Task<BotReply> ProfileCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct)
    {
        var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
        var d = s.Prof;
        switch (p.Length > 1 ? p[1] : "")
        {
            case "man":
                return ManualGoalScreen((await ProfileAsync(member.Id, ct))?.Kcal ?? 2000);
            case "start":
            {
                d.Clear();
                s.Step = Step.None;
                return SexScreen(d);
            }
            case "sx":
                d.Sex = p[2] == "F" ? Sex.Female : Sex.Male;
                s.Step = Step.ProfAge;
                return new BotReply(Steps("📝 Шаг 2 из 6 · Возраст", d, "Сколько вам лет? Напишите число, например: 28"));
            case "ac":
            {
                d.Activity = double.Parse(p[2], CultureInfo.InvariantCulture);
                return GoalScreen(d);
            }
            case "go":
            {
                d.Goal = (DietGoal)int.Parse(p[2], CultureInfo.InvariantCulture);
                return await SaveProfileAsync(member, d, ct);
            }
            default:
                return await GoalHomeAsync(member, ct);
        }
    }

    private static BotReply SexScreen(ProfileDraft d) => new(
        Steps("📝 Шаг 1 из 6 · Пол", d, "Выберите пол (он нужен для формулы расчёта):"),
        new[] { Row(new InlineButton("👨 Мужской", "g:sx:M"), new InlineButton("👩 Женский", "g:sx:F")),
                Row(new InlineButton("⬅️ Отмена", "t:goal")) });

    private static BotReply ActivityScreen(ProfileDraft d)
    {
        var rows = EnergyCalculator.ActivityLevels.Select(a =>
                Row(new InlineButton($"{a.Label} · ≈{Estimate(d, activityOverride: a.Factor):F0}",
                    $"g:ac:{a.Factor.ToString(CultureInfo.InvariantCulture)}")))
            .ToList();
        rows.Add(Row(new InlineButton("⬅️ Отмена", "t:goal")));
        double bmi = EnergyCalculator.Bmi(d.Weight ?? DefWeight, d.Height ?? DefHeight);
        return new BotReply(Steps("📝 Шаг 5 из 6 · Активность", d,
            $"Ваш ИМТ: {bmi:F1} ({EnergyCalculator.BmiCategory(bmi)})\n\nНасколько вы активны? (в кнопках — норма на день без изменения веса)"), rows);
    }

    private static BotReply GoalScreen(ProfileDraft d)
    {
        double bmi = EnergyCalculator.Bmi(d.Weight ?? DefWeight, d.Height ?? DefHeight);
        var rows = new List<IReadOnlyList<InlineButton>>();
        foreach (var g in new[] { DietGoal.Lose, DietGoal.Maintain, DietGoal.Gain })
            rows.Add(Row(new InlineButton($"{EnergyCalculator.GoalLabel(g)} · {Estimate(d, goalOverride: g):F0} ккал", $"g:go:{(int)g}")));
        rows.Add(Row(new InlineButton("⬅️ Отмена", "t:goal")));
        string hint = bmi < 18.5 ? "\n⚠️ ИМТ ниже нормы — худеть не рекомендуется." :
                      bmi >= 25 ? "\nПри ИМТ выше нормы обычно выбирают «Похудеть»." : "";
        return new BotReply(Steps("📝 Шаг 6 из 6 · Цель", d,
            $"Ваш ИМТ: {bmi:F1} ({EnergyCalculator.BmiCategory(bmi)}){hint}\n\nЧто вы хотите? (в кнопках — норма на день)"), rows);
    }

    private async Task<BotReply> SaveProfileAsync(Member member, ProfileDraft d, CancellationToken ct)
    {
        var sex = d.Sex ?? DefSex; int age = d.Age ?? DefAge; double h = d.Height ?? DefHeight, w = d.Weight ?? DefWeight;
        double act = d.Activity ?? DefActivity; var goal = d.Goal ?? DietGoal.Maintain;
        double kcal = EnergyCalculator.DailyTarget(sex, age, h, w, act, goal);

        var t = await _db.MemberTargets.FirstOrDefaultAsync(x => x.MemberId == member.Id, ct);
        if (t is null) { t = new MemberTarget { MemberId = member.Id }; _db.MemberTargets.Add(t); }
        t.Sex = sex; t.AgeYears = age; t.HeightCm = h; t.WeightKg = w; t.Activity = act; t.Goal = goal;
        t.HasProfile = true; t.IsManual = false; t.Kcal = kcal;
        await _db.SaveChangesAsync(ct);

        double bmi = EnergyCalculator.Bmi(w, h);
        double bmr = EnergyCalculator.Bmr(sex, age, h, w), tdee = bmr * act;
        var (pr, fr, cr) = EnergyCalculator.MacroShares(goal);
        var sb = new StringBuilder("✅ Норма рассчитана\n\n");
        sb.AppendLine($"ИМТ: {bmi:F1} ({EnergyCalculator.BmiCategory(bmi)})");
        sb.AppendLine($"Основной обмен: {bmr:F0} ккал");
        sb.AppendLine($"С учётом активности: {tdee:F0} ккал");
        sb.AppendLine($"Цель: {EnergyCalculator.GoalLabel(goal)}\n");
        sb.AppendLine($"🔥 Норма на день: {kcal:F0} ккал");
        sb.AppendLine($"Белки {kcal * pr / 4:F0} г · Жиры {kcal * fr / 9:F0} г · Углеводы {kcal * cr / 4:F0} г");
        sb.Append("\nРасчёт ориентировочный и не заменяет консультацию врача.");
        return new BotReply(sb.ToString(), new[] { Row(new InlineButton("📅 К дневнику", "t:day"), new InlineButton("🔁 Пересчитать", "g:start")) });
    }

    private BotReply? ProfileTyped(Session s, string text)
    {
        var d = s.Prof;
        var m = Regex.Match(text.Trim(), @"^(?<n>\d+(?:[.,]\d+)?)\s*(?:лет|года|год|см|кг|г)?\.?$", RegexOptions.IgnoreCase);
        double? v = m.Success ? double.Parse(m.Groups["n"].Value.Replace(',', '.'), CultureInfo.InvariantCulture) : null;

        switch (s.Step)
        {
            case Step.ProfAge:
                if (v is null or < 14 or > 100) return new BotReply("Напишите возраст числом от 14 до 100, например: 28");
                d.Age = (int)Math.Round(v.Value);
                s.Step = Step.ProfHeight;
                return new BotReply(Steps("📝 Шаг 3 из 6 · Рост", d, "Какой у вас рост в сантиметрах? Например: 175"));
            case Step.ProfHeight:
                if (v is null or < 120 or > 230) return new BotReply("Напишите рост в сантиметрах от 120 до 230, например: 175");
                d.Height = v.Value;
                s.Step = Step.ProfWeight;
                return new BotReply(Steps("📝 Шаг 4 из 6 · Вес", d, "Сколько вы весите, в кг? Например: 72 или 72,5"));
            case Step.ProfWeight:
                if (v is null or < 30 or > 300) return new BotReply("Напишите вес в килограммах от 30 до 300, например: 72,5");
                d.Weight = v.Value;
                s.Step = Step.None;
                return ActivityScreen(d);
        }
        return null;
    }
}
