using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MealPlanner.App.Data;
using MealPlanner.App.Services;
using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Parsing;
using MealPlanner.Core.Planning;
using MealPlanner.Core.Recommendation;

namespace MealPlanner.App.Bot;

/// <summary>
/// Text commands of the bot (/dish, /have, /menu and so on), independent of Telegram: takes text and returns reply text
/// The command registry is a name-to-handler table, so a new command is added without changing the dispatcher
/// </summary>
public class CommandHandler
{
    private const string Num = @"\d+(?:[.,]\d+)?";
    private static readonly Regex FoodRx = new(
        $@"^(?<name>.+?)\s+(?<k>{Num})\s+(?<p>{Num})\s+(?<f>{Num})\s+(?<c>{Num})(?:\s+(?<price>{Num}))?$",
        RegexOptions.Compiled);

    private record Call(Member Member, string Arg, IReadOnlyList<Food> Foods);

    private delegate Task<string> Handler(Call call, CancellationToken ct);

    private readonly AppServices _s;
    private readonly Dictionary<string, Handler> _commands = new();

    public CommandHandler(AppServices services)
    {
        _s = services;
        Register(new[] { "/start" }, (c, _) => Task.FromResult(Welcome(c.Member)));
        Register(new[] { "/help", "/помощь" }, (_, _) => Task.FromResult(Help()));
        Register(new[] { "/food", "/продукт" }, (c, ct) => AddFoodAsync(c.Arg, ct));
        Register(new[] { "/dish", "/блюдо" }, AddDishAsync);
        Register(new[] { "/calc", "/калории" }, (c, _) => Task.FromResult(Calc(c.Arg, c.Foods)));
        Register(new[] { "/dishes", "/блюда" }, ListDishesAsync);
        Register(new[] { "/delete", "/удалить" }, DeleteDishAsync);
        Register(new[] { "/have", "/есть" }, (c, ct) => ChangePantryAsync(c, replace: false, ct));
        Register(new[] { "/set", "/установить" }, (c, ct) => ChangePantryAsync(c, replace: true, ct));
        Register(new[] { "/pantry", "/запасы" }, ShowPantryAsync);
        Register(new[] { "/cooked", "/приготовил" }, CookedAsync);
        Register(new[] { "/cook", "/приготовить" }, CookAsync);
        Register(new[] { "/idea", "/идея" }, IdeaAsync);
        Register(new[] { "/menu", "/меню" }, MenuAsync);
    }

    private void Register(string[] names, Handler handler)
    {
        foreach (var name in names) _commands[name] = handler;
    }

    public async Task<string> HandleAsync(long userId, string userName, string text, CancellationToken ct = default)
    {
        var (cmd, arg) = Split(text);
        if (cmd == "/join") return await JoinAsync(userId, userName, arg, ct);

        var member = await _s.Households.EnsureMemberAsync(userId, userName, ct);
        var foods = await _s.Foods.ListAsync(ct);
        return _commands.TryGetValue(cmd, out var handler)
            ? await handler(new Call(member, arg, foods), ct)
            : "Неизвестная команда.\n\n" + Help();
    }

    public async Task<string> JoinAsync(long userId, string userName, string code, CancellationToken ct)
    {
        var result = await _s.Households.JoinAsync(userId, userName, code, ct);
        return result.Found
            ? $"Готово! Вы в общем домохозяйстве {result.Code}. Участников: {result.MemberCount}. Блюда и запасы теперь общие."
            : "Домохозяйство с таким кодом не найдено. Код выглядит как ABC123 (его показывает /start).";
    }

    internal static (string Cmd, string Arg) Split(string text)
    {
        string t = (text ?? string.Empty).Trim();
        if (t.StartsWith('/'))
        {
            int sp = t.IndexOfAny(new[] { ' ', '\n' });
            string cmd = (sp < 0 ? t : t[..sp]).ToLowerInvariant();
            int at = cmd.IndexOf('@');
            if (at > 0) cmd = cmd[..at];
            return (cmd, sp < 0 ? string.Empty : t[(sp + 1)..].Trim());
        }
        return t.Contains(':') ? ("/dish", t) : ("/help", string.Empty);
    }

    private static double ParseNum(string s) =>
        double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);

    private static string Welcome(Member m) =>
        $"Привет, {m.Name}! Я помогу считать калории и решать, что приготовить.\n" +
        $"Код вашего домохозяйства: {m.Household!.Code}\n" +
        "Пусть партнёр отправит мне: /join " + m.Household.Code + " — и у вас будут общие блюда и запасы.\n\n" + Help();

    private static string Help() =>
        "Команды:\n" +
        "/dish борщ: свёкла 200, картофель 300; 4 порции — сохранить блюдо и посчитать калории\n" +
        "/calc омлет: яйца 3 шт, молоко 100 — только посчитать, не сохраняя\n" +
        "/dishes — список блюд, /delete 2 — удалить блюдо №2\n" +
        "/have курица 500, рис 300 — добавить в запасы (/set — задать точное количество, 0 — убрать)\n" +
        "/pantry — что есть, /cooked 2 — списать продукты после готовки блюда №2\n" +
        "/cook [ккал] — что приготовить из того, что есть (можно указать желаемые ккал на порцию)\n" +
        "/idea — идея, если не знаете, что готовить\n" +
        "/menu [ккал] — меню на день из запасов (по умолчанию 2000 ккал)\n" +
        "/food название ккал белки жиры углеводы [цена] — добавить новый продукт (значения на 100 г)\n" +
        "/join КОД — присоединиться к домохозяйству партнёра";

    private async Task<string> AddFoodAsync(string arg, CancellationToken ct)
    {
        var m = FoodRx.Match(arg.Trim());
        if (!m.Success)
            return "Формат: /food название ккал белки жиры углеводы [цена за 100 г]\nНапример: /food Сыр фета 264 14 21 4 1.2";

        var values = new Food
        {
            Name = m.Groups["name"].Value.Trim(),
            Category = FoodRepository.CustomCategory,
            MaxGrams = FoodRepository.DefaultMaxGrams,
            KcalPer100 = ParseNum(m.Groups["k"].Value),
            ProteinPer100 = ParseNum(m.Groups["p"].Value),
            FatPer100 = ParseNum(m.Groups["f"].Value),
            CarbsPer100 = ParseNum(m.Groups["c"].Value),
            PricePer100 = m.Groups["price"].Success ? ParseNum(m.Groups["price"].Value) : FoodRepository.DefaultPrice
        };
        var (_, created) = await _s.Foods.UpsertAsync(values, ct);
        return created ? $"Продукт «{values.Name}» добавлен." : $"Продукт «{values.Name}» обновлён.";
    }

    private static string DescribeParseProblems(ParsedDish p)
    {
        var sb = new StringBuilder();
        foreach (var e in p.Errors) sb.AppendLine("• " + e);
        if (p.UnknownFoods.Count > 0)
        {
            sb.AppendLine("• Не знаю продукты: " + string.Join(", ", p.UnknownFoods));
            sb.AppendLine("  Добавьте их: /food название ккал белки жиры углеводы [цена] (на 100 г) — и отправьте блюдо ещё раз.");
        }
        return sb.ToString().TrimEnd();
    }

    private static string Describe(Dish d) =>
        $"На порцию: {Nutr(d.PerServing)}\nВсего ({d.Servings} порц.): {Nutr(d.Total)}\nСтоимость ≈ {d.Cost:F2} €";

    private static string Nutr(Nutrients n) =>
        $"{n.Kcal:F0} ккал, Б {n.Protein:F0} / Ж {n.Fat:F0} / У {n.Carbs:F0}";

    private static string Calc(string arg, IReadOnlyList<Food> foods)
    {
        var p = DishParser.Parse(arg, foods);
        return p.IsValid ? $"«{p.Name}»\n" + Describe(new Dish(p.Name, p.Servings, p.Ingredients)) : DescribeParseProblems(p);
    }

    private async Task<string> AddDishAsync(Call call, CancellationToken ct)
    {
        var p = DishParser.Parse(call.Arg, call.Foods);
        if (!p.IsValid) return DescribeParseProblems(p);

        var dish = new Dish(p.Name, p.Servings, p.Ingredients);
        bool replaced = await _s.Dishes.SaveAsync(call.Member.HouseholdId, dish, ct);
        return $"Блюдо «{p.Name}» сохранено" + (replaced ? " (заменило прежнее)" : "") + ".\n" + Describe(dish);
    }

    private async Task<string> ListDishesAsync(Call call, CancellationToken ct)
    {
        var dishes = await _s.Dishes.ListAsync(call.Member.HouseholdId, call.Foods, ct);
        if (dishes.Count == 0) return "Блюд пока нет. Добавьте: /dish борщ: свёкла 200, картофель 300; 4 порции";

        var sb = new StringBuilder("Ваши блюда:\n");
        for (int i = 0; i < dishes.Count; i++)
        {
            var d = dishes[i].Model;
            sb.AppendLine($"{i + 1}. {d.Name} — {d.PerServing.Kcal:F0} ккал/порция ({d.Servings} порц.)");
        }
        return sb.ToString().TrimEnd();
    }

    private async Task<string> DeleteDishAsync(Call call, CancellationToken ct)
    {
        if (!int.TryParse(call.Arg.Trim(), out int n) || n < 1) return "Укажите номер блюда из /dishes, например: /delete 2";

        var dishes = await _s.Dishes.ListAsync(call.Member.HouseholdId, call.Foods, ct);
        if (n > dishes.Count) return "Блюда с таким номером нет.";

        string? name = await _s.Dishes.DeleteAsync(call.Member.HouseholdId, dishes[n - 1].Entity.Id, ct);
        return $"Блюдо «{name}» удалено.";
    }

    private async Task<string> ChangePantryAsync(Call call, bool replace, CancellationToken ct)
    {
        string arg = call.Arg.Trim();
        int householdId = call.Member.HouseholdId;
        if (arg.Equals("clear", StringComparison.OrdinalIgnoreCase) || arg.Equals("очистить", StringComparison.OrdinalIgnoreCase))
        {
            await _s.Pantry.ClearAsync(householdId, ct);
            return "Запасы очищены.";
        }

        var parsed = DishParser.ParseItems(arg, call.Foods);
        var problems = new StringBuilder();
        foreach (var e in parsed.Errors) problems.AppendLine("• " + e);
        if (parsed.UnknownFoods.Count > 0)
            problems.AppendLine("• Не знаю продукты: " + string.Join(", ", parsed.UnknownFoods) + " (добавьте через /food)");
        if (parsed.Items.Count == 0)
            return problems.Length > 0 ? problems.ToString().TrimEnd() : "Формат: /have курица 500, рис 300";

        await _s.Pantry.ChangeAsync(householdId, parsed.Items.Select(i => (i.Food.Id, i.Grams)), replace, ct);

        string summary = string.Join(", ", parsed.Items.Select(i => $"{i.Food.Name} {i.Grams:F0} г"));
        return (replace ? "Установлено: " : "Добавлено: ") + summary +
               (problems.Length > 0 ? "\n\nНе удалось:\n" + problems.ToString().TrimEnd() : string.Empty);
    }

    private async Task<string> ShowPantryAsync(Call call, CancellationToken ct)
    {
        var entries = await _s.Pantry.ListAsync(call.Member.HouseholdId, ct);
        return entries.Count == 0
            ? "Запасов пока нет. Добавьте: /have курица 500, рис 300"
            : "Запасы:\n" + string.Join("\n", entries.Select(e => $"• {e.Food!.Name} — {e.Grams:F0} г"));
    }

    private async Task<string> CookedAsync(Call call, CancellationToken ct)
    {
        var parts = call.Arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out int n) || n < 1)
            return "Укажите номер блюда из /dishes: /cooked 2 [сколько порций]";

        var dishes = await _s.Dishes.ListAsync(call.Member.HouseholdId, call.Foods, ct);
        if (n > dishes.Count) return "Блюда с таким номером нет.";
        var dish = dishes[n - 1].Model;

        double portions = dish.Servings;
        if (parts.Length > 1 && TryParsePositive(parts[1], out var custom)) portions = custom;

        await _s.Pantry.ConsumeAsync(call.Member.HouseholdId, dish, portions, ct);
        return $"Списано с запасов: «{dish.Name}», {portions:0.#} порц. Приятного аппетита!";
    }

    private static bool TryParsePositive(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0;

    private static string DescribeRecommendation(int index, DishRecommendation r)
    {
        var d = r.Dish;
        string head = $"{index}. {d.Name} — {d.PerServing.Kcal:F0} ккал/порция";
        if (r.Coverage.IsCookable) return head + " — можно готовить сейчас";

        var miss = r.Coverage.Missing.OrderByDescending(m => m.Cost).Take(3)
            .Select(m => $"{m.Food.Name} {m.MissingGrams:F0} г");
        int more = Math.Max(0, r.Coverage.Missing.Count - 3);
        return head + $"\n   не хватает: {string.Join(", ", miss)}" + (more > 0 ? $" и ещё {more}" : "") +
               $" (≈ {r.Coverage.MissingCost:F2} €)";
    }

    private async Task<string> CookAsync(Call call, CancellationToken ct)
    {
        var dishes = (await _s.Dishes.ListAsync(call.Member.HouseholdId, call.Foods, ct)).Select(d => d.Model).ToList();
        if (dishes.Count == 0) return "У вас пока нет сохранённых блюд. Добавьте через /dish или попросите /idea.";

        double? target = TryParsePositive(call.Arg, out var t) ? t : null;
        var pantry = await _s.Pantry.LoadAsync(call.Member.HouseholdId, ct);
        if (pantry.Count == 0)
            return "Запасов нет, поэтому сравнивать не с чем. Добавьте продукты: /have курица 500, рис 300 — или попросите /idea.";

        var top = new SmartRecommender().Recommend(dishes, pantry, new RecommendationContext(target), 5);
        var sb = new StringBuilder(target is null ? "Что приготовить из ваших блюд:\n" : $"Что приготовить (цель ≈ {target:F0} ккал на порцию):\n");
        for (int i = 0; i < top.Count; i++) sb.AppendLine(DescribeRecommendation(i + 1, top[i]));
        return sb.ToString().TrimEnd();
    }

    private async Task<string> IdeaAsync(Call call, CancellationToken ct)
    {
        var own = (await _s.Dishes.ListAsync(call.Member.HouseholdId, call.Foods, ct)).Select(d => d.Model).ToList();
        var pool = own.Concat(TemplateDishes.Build(call.Foods)
            .Where(t => !own.Any(o => o.Name.Equals(t.Name, StringComparison.CurrentCultureIgnoreCase)))).ToList();
        var pantry = await _s.Pantry.LoadAsync(call.Member.HouseholdId, ct);

        IDishRecommender rec = pantry.Count > 0 ? new SmartRecommender() : new RandomRecommender(Random.Shared.Next());
        var top = rec.Recommend(pool, pantry, new RecommendationContext(), 3);

        var sb = new StringBuilder(pantry.Count > 0 ? "Идеи с учётом ваших запасов:\n" : "Запасов нет — вот случайные идеи:\n");
        for (int i = 0; i < top.Count; i++)
        {
            sb.AppendLine(DescribeRecommendation(i + 1, top[i]));
            sb.AppendLine("   состав: " + string.Join(", ", top[i].Dish.Ingredients.Select(x => $"{x.Food.Name} {x.Grams:F0} г")));
        }
        sb.Append("Понравилось? Сохраните блюдо через /dish, чтобы оно появилось в вашем списке.");
        return sb.ToString();
    }

    private async Task<string> MenuAsync(Call call, CancellationToken ct)
    {
        double kcal = TryParsePositive(call.Arg, out var k) && k is >= 800 and <= 6000 ? k : 2000;
        var targets = NutrientTargets.ForKcal(kcal);

        var pantry = await _s.Pantry.LoadAsync(call.Member.HouseholdId, ct);
        var limited = call.Foods.Where(f => pantry.ContainsKey(f.Id))
            .Select(f => new Food
            {
                Id = f.Id, Name = f.Name, Category = f.Category,
                KcalPer100 = f.KcalPer100, ProteinPer100 = f.ProteinPer100, FatPer100 = f.FatPer100,
                CarbsPer100 = f.CarbsPer100, PricePer100 = f.PricePer100,
                MaxGrams = Math.Min(f.MaxGrams, pantry[f.Id])
            }).ToList();

        var planner = new LpPlanner();
        if (limited.Count > 0)
        {
            var plan = planner.Plan(limited, targets);
            if (plan.IsFeasible)
                return $"Меню на день из ваших запасов (≈ {kcal:F0} ккал):\n" + FormatPlan(plan);
        }

        var full = planner.Plan(call.Foods, targets);
        if (!full.IsFeasible) return "Не удалось подобрать меню под такие ограничения. Попробуйте другую калорийность.";

        var buy = full.Items
            .Select(i => (i.Food, Need: i.Grams - (pantry.TryGetValue(i.Food.Id, out var h) ? h : 0)))
            .Where(x => x.Need > 1)
            .Select(x => $"{x.Food.Name} {x.Need:F0} г");
        return "Из одних запасов день не собрать. Вот сбалансированное меню из каталога:\n" + FormatPlan(full) +
               "\n\nЧтобы его приготовить, докупите: " + string.Join(", ", buy);
    }

    private static string FormatPlan(MenuPlan plan) =>
        string.Join("\n", plan.Items.OrderByDescending(i => i.Nutrients.Kcal).Select(i => $"• {i.Food.Name} — {i.Grams:F0} г")) +
        $"\nИтого: {Nutr(plan.Totals)}, стоимость ≈ {plan.TotalCost:F2} €";
}
