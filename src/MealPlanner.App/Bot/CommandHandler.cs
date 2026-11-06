using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MealPlanner.App.Data;
using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Parsing;
using MealPlanner.Core.Planning;
using MealPlanner.Core.Recommendation;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.App.Bot;

/// <summary>
/// Bot command logic independent of Telegram: takes message text and returns reply text
/// This allows testing the bot without a network
/// </summary>
public class CommandHandler
{
    private const string Num = @"\d+(?:[.,]\d+)?";
    private static readonly Regex FoodRx = new(
        $@"^(?<name>.+?)\s+(?<k>{Num})\s+(?<p>{Num})\s+(?<f>{Num})\s+(?<c>{Num})(?:\s+(?<price>{Num}))?$",
        RegexOptions.Compiled);

    private readonly AppDbContext _db;

    public CommandHandler(AppDbContext db) => _db = db;

    public async Task<string> HandleAsync(long userId, string userName, string text, CancellationToken ct = default)
    {
        var (cmd, arg) = Split(text);

        if (cmd == "/join") return await JoinAsync(userId, userName, arg, ct);

        var member = await EnsureMemberAsync(userId, userName, ct);
        var foods = await _db.Foods.AsNoTracking().ToListAsync(ct);

        switch (cmd)
        {
            case "/start": return Welcome(member);
            case "/help":
            case "/помощь": return Help();
            case "/food":
            case "/продукт": return await AddFoodAsync(arg, foods, ct);
            case "/dish":
            case "/блюдо": return await AddDishAsync(member, arg, foods, ct);
            case "/calc":
            case "/калории": return Calc(arg, foods);
            case "/dishes":
            case "/блюда": return await ListDishesAsync(member, foods, ct);
            case "/delete":
            case "/удалить": return await DeleteDishAsync(member, arg, ct);
            case "/have":
            case "/есть": return await ChangePantryAsync(member, arg, foods, replace: false, ct);
            case "/set":
            case "/установить": return await ChangePantryAsync(member, arg, foods, replace: true, ct);
            case "/pantry":
            case "/запасы": return await ShowPantryAsync(member, ct);
            case "/cooked":
            case "/приготовил": return await CookedAsync(member, arg, foods, ct);
            case "/cook":
            case "/приготовить": return await CookAsync(member, arg, foods, ct);
            case "/idea":
            case "/идея": return await IdeaAsync(member, foods, ct);
            case "/menu":
            case "/меню": return await MenuAsync(member, arg, foods, ct);
            default: return "Неизвестная команда.\n\n" + Help();
        }
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

    internal async Task<Member> EnsureMemberAsync(long userId, string userName, CancellationToken ct)
    {
        var m = await _db.Members.Include(x => x.Household)
            .FirstOrDefaultAsync(x => x.TelegramUserId == userId, ct);
        if (m != null) return m;

        var household = new Household { Code = await NewCodeAsync(ct) };
        m = new Member { TelegramUserId = userId, Name = userName, Household = household };
        _db.Members.Add(m);
        await _db.SaveChangesAsync(ct);
        return m;
    }

    private async Task<string> NewCodeAsync(CancellationToken ct)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        while (true)
        {
            string code = new(Enumerable.Range(0, 6).Select(_ => alphabet[Random.Shared.Next(alphabet.Length)]).ToArray());
            if (!await _db.Households.AnyAsync(h => h.Code == code, ct)) return code;
        }
    }

    internal async Task<string> JoinAsync(long userId, string userName, string code, CancellationToken ct)
    {
        code = code.Trim().ToUpperInvariant();
        var household = await _db.Households.FirstOrDefaultAsync(h => h.Code == code, ct);
        if (household is null) return "Домохозяйство с таким кодом не найдено. Код выглядит как ABC123 (его показывает /start).";

        var m = await _db.Members.FirstOrDefaultAsync(x => x.TelegramUserId == userId, ct);
        if (m is null)
            _db.Members.Add(new Member { TelegramUserId = userId, Name = userName, HouseholdId = household.Id });
        else
            m.HouseholdId = household.Id;
        await _db.SaveChangesAsync(ct);

        int count = await _db.Members.CountAsync(x => x.HouseholdId == household.Id, ct);
        return $"Готово! Вы в общем домохозяйстве {household.Code}. Участников: {count}. Блюда и запасы теперь общие.";
    }

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

    private async Task<string> AddFoodAsync(string arg, IReadOnlyList<Food> foods, CancellationToken ct)
    {
        var m = FoodRx.Match(arg.Trim());
        if (!m.Success)
            return "Формат: /food название ккал белки жиры углеводы [цена за 100 г]\nНапример: /food Сыр фета 264 14 21 4 1.2";

        string name = m.Groups["name"].Value.Trim();
        var existing = foods.FirstOrDefault(f => FoodMatcher.Normalize(f.Name) == FoodMatcher.Normalize(name));

        var food = existing is null
            ? new Food { Name = name, Category = "Свои продукты", MaxGrams = 400 }
            : await _db.Foods.FirstAsync(f => f.Id == existing.Id, ct);

        food.KcalPer100 = ParseNum(m.Groups["k"].Value);
        food.ProteinPer100 = ParseNum(m.Groups["p"].Value);
        food.FatPer100 = ParseNum(m.Groups["f"].Value);
        food.CarbsPer100 = ParseNum(m.Groups["c"].Value);
        food.PricePer100 = m.Groups["price"].Success ? ParseNum(m.Groups["price"].Value) : (existing?.PricePer100 ?? 0.3);

        if (existing is null) _db.Foods.Add(food);
        await _db.SaveChangesAsync(ct);
        return existing is null ? $"Продукт «{name}» добавлен." : $"Продукт «{name}» обновлён.";
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

    private string Calc(string arg, IReadOnlyList<Food> foods)
    {
        var p = DishParser.Parse(arg, foods);
        if (!p.IsValid) return DescribeParseProblems(p);
        return $"«{p.Name}»\n" + Describe(new Dish(p.Name, p.Servings, p.Ingredients));
    }

    private async Task<string> AddDishAsync(Member member, string arg, IReadOnlyList<Food> foods, CancellationToken ct)
    {
        var p = DishParser.Parse(arg, foods);
        if (!p.IsValid) return DescribeParseProblems(p);

        var old = await _db.Dishes
            .Where(d => d.HouseholdId == member.HouseholdId)
            .ToListAsync(ct);
        var same = old.FirstOrDefault(d => d.Name.Equals(p.Name, StringComparison.CurrentCultureIgnoreCase));
        if (same != null) _db.Dishes.Remove(same);

        _db.Dishes.Add(new HouseholdDish
        {
            HouseholdId = member.HouseholdId,
            Name = p.Name,
            Servings = p.Servings,
            Ingredients = p.Ingredients
                .GroupBy(i => i.Food.Id)
                .Select(g => new HouseholdDishIngredient { FoodId = g.Key, Grams = g.Sum(i => i.Grams) })
                .ToList()
        });
        await _db.SaveChangesAsync(ct);

        var dish = new Dish(p.Name, p.Servings, p.Ingredients);
        return $"Блюдо «{p.Name}» сохранено" + (same != null ? " (заменило прежнее)" : "") + ".\n" + Describe(dish);
    }

    internal async Task<List<(HouseholdDish Entity, Dish Model)>> LoadDishesAsync(
        int householdId, IReadOnlyList<Food> foods, CancellationToken ct)
    {
        var map = foods.ToDictionary(f => f.Id);
        var entities = await _db.Dishes.AsNoTracking()
            .Include(d => d.Ingredients)
            .Where(d => d.HouseholdId == householdId)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        return entities
            .Select(e => (e, new Dish(e.Name, e.Servings,
                e.Ingredients.Select(i => new DishIngredient(map[i.FoodId], i.Grams)).ToList(), e.CookMinutes, e.Difficulty)))
            .ToList();
    }

    private async Task<string> ListDishesAsync(Member member, IReadOnlyList<Food> foods, CancellationToken ct)
    {
        var dishes = await LoadDishesAsync(member.HouseholdId, foods, ct);
        if (dishes.Count == 0) return "Блюд пока нет. Добавьте: /dish борщ: свёкла 200, картофель 300; 4 порции";

        var sb = new StringBuilder("Ваши блюда:\n");
        for (int i = 0; i < dishes.Count; i++)
        {
            var d = dishes[i].Model;
            sb.AppendLine($"{i + 1}. {d.Name} — {d.PerServing.Kcal:F0} ккал/порция ({d.Servings} порц.)");
        }
        return sb.ToString().TrimEnd();
    }

    private async Task<string> DeleteDishAsync(Member member, string arg, CancellationToken ct)
    {
        if (!int.TryParse(arg.Trim(), out int n) || n < 1) return "Укажите номер блюда из /dishes, например: /delete 2";

        var list = await _db.Dishes.Where(d => d.HouseholdId == member.HouseholdId).OrderBy(d => d.Id).ToListAsync(ct);
        if (n > list.Count) return "Блюда с таким номером нет.";

        string name = list[n - 1].Name;
        _db.Dishes.Remove(list[n - 1]);
        await _db.SaveChangesAsync(ct);
        return $"Блюдо «{name}» удалено.";
    }

    private async Task<string> ChangePantryAsync(
        Member member, string arg, IReadOnlyList<Food> foods, bool replace, CancellationToken ct)
    {
        if (arg.Trim().Equals("clear", StringComparison.OrdinalIgnoreCase) ||
            arg.Trim().Equals("очистить", StringComparison.OrdinalIgnoreCase))
        {
            _db.Pantry.RemoveRange(_db.Pantry.Where(p => p.HouseholdId == member.HouseholdId));
            await _db.SaveChangesAsync(ct);
            return "Запасы очищены.";
        }

        var parsed = DishParser.ParseItems(arg, foods);
        var problems = new StringBuilder();
        foreach (var e in parsed.Errors) problems.AppendLine("• " + e);
        if (parsed.UnknownFoods.Count > 0)
            problems.AppendLine("• Не знаю продукты: " + string.Join(", ", parsed.UnknownFoods) + " (добавьте через /food)");
        if (parsed.Items.Count == 0)
            return problems.Length > 0 ? problems.ToString().TrimEnd() : "Формат: /have курица 500, рис 300";

        var entries = await _db.Pantry.Where(p => p.HouseholdId == member.HouseholdId).ToListAsync(ct);
        foreach (var item in parsed.Items)
        {
            var entry = entries.FirstOrDefault(e => e.FoodId == item.Food.Id);
            if (entry is null)
            {
                entry = new PantryEntry { HouseholdId = member.HouseholdId, FoodId = item.Food.Id, Grams = 0 };
                _db.Pantry.Add(entry);
                entries.Add(entry);
            }
            entry.Grams = replace ? item.Grams : entry.Grams + item.Grams;
        }
        foreach (var stale in entries.Where(x => x.Grams <= 0).ToList())
        {
            if (_db.Entry(stale).State == EntityState.Added) _db.Entry(stale).State = EntityState.Detached;
            else _db.Pantry.Remove(stale);
        }
        await _db.SaveChangesAsync(ct);

        string summary = string.Join(", ", parsed.Items.Select(i => $"{i.Food.Name} {i.Grams:F0} г"));
        return (replace ? "Установлено: " : "Добавлено: ") + summary +
               (problems.Length > 0 ? "\n\nНе удалось:\n" + problems.ToString().TrimEnd() : string.Empty);
    }

    private async Task<string> ShowPantryAsync(Member member, CancellationToken ct)
    {
        var entries = await _db.Pantry.AsNoTracking().Include(p => p.Food)
            .Where(p => p.HouseholdId == member.HouseholdId && p.Grams > 0)
            .OrderBy(p => p.Food!.Name).ToListAsync(ct);
        if (entries.Count == 0) return "Запасов пока нет. Добавьте: /have курица 500, рис 300";

        return "Запасы:\n" + string.Join("\n", entries.Select(e => $"• {e.Food!.Name} — {e.Grams:F0} г"));
    }

    private async Task<Dictionary<int, double>> LoadPantryAsync(int householdId, CancellationToken ct) =>
        await _db.Pantry.AsNoTracking()
            .Where(p => p.HouseholdId == householdId && p.Grams > 0)
            .ToDictionaryAsync(p => p.FoodId, p => p.Grams, ct);

    private async Task<string> CookedAsync(Member member, string arg, IReadOnlyList<Food> foods, CancellationToken ct)
    {
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out int n) || n < 1)
            return "Укажите номер блюда из /dishes: /cooked 2 [сколько порций]";

        var dishes = await LoadDishesAsync(member.HouseholdId, foods, ct);
        if (n > dishes.Count) return "Блюда с таким номером нет.";
        var dish = dishes[n - 1].Model;

        double portions = dish.Servings;
        if (parts.Length > 1 && double.TryParse(parts[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && p > 0)
            portions = p;
        double k = portions / dish.Servings;

        var entries = await _db.Pantry.Where(e => e.HouseholdId == member.HouseholdId).ToListAsync(ct);
        foreach (var ing in dish.Ingredients)
        {
            var entry = entries.FirstOrDefault(e => e.FoodId == ing.Food.Id);
            if (entry is null) continue;
            entry.Grams -= ing.Grams * k;
            if (entry.Grams <= 0) _db.Pantry.Remove(entry);
        }
        await _db.SaveChangesAsync(ct);
        return $"Списано с запасов: «{dish.Name}», {portions:0.#} порц. Приятного аппетита!";
    }

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

    private async Task<string> CookAsync(Member member, string arg, IReadOnlyList<Food> foods, CancellationToken ct)
    {
        var dishes = (await LoadDishesAsync(member.HouseholdId, foods, ct)).Select(d => d.Model).ToList();
        if (dishes.Count == 0) return "У вас пока нет сохранённых блюд. Добавьте через /dish или попросите /idea.";

        double? target = double.TryParse(arg.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t > 0 ? t : null;
        var pantry = await LoadPantryAsync(member.HouseholdId, ct);
        if (pantry.Count == 0)
            return "Запасов нет, поэтому сравнивать не с чем. Добавьте продукты: /have курица 500, рис 300 — или попросите /idea.";

        var top = new SmartRecommender().Recommend(dishes, pantry, new RecommendationContext(target), 5);
        var sb = new StringBuilder(target is null ? "Что приготовить из ваших блюд:\n" : $"Что приготовить (цель ≈ {target:F0} ккал на порцию):\n");
        for (int i = 0; i < top.Count; i++) sb.AppendLine(DescribeRecommendation(i + 1, top[i]));
        return sb.ToString().TrimEnd();
    }

    private async Task<string> IdeaAsync(Member member, IReadOnlyList<Food> foods, CancellationToken ct)
    {
        var own = (await LoadDishesAsync(member.HouseholdId, foods, ct)).Select(d => d.Model).ToList();
        var pool = own.Concat(TemplateDishes.Build(foods)
            .Where(t => !own.Any(o => o.Name.Equals(t.Name, StringComparison.CurrentCultureIgnoreCase)))).ToList();
        var pantry = await LoadPantryAsync(member.HouseholdId, ct);

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

    private async Task<string> MenuAsync(Member member, string arg, IReadOnlyList<Food> foods, CancellationToken ct)
    {
        double kcal = double.TryParse(arg.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var k) && k >= 800 && k <= 6000
            ? k : 2000;
        var targets = NutrientTargets.ForKcal(kcal);

        var pantry = await LoadPantryAsync(member.HouseholdId, ct);
        var limited = foods.Where(f => pantry.ContainsKey(f.Id))
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

        var full = planner.Plan(foods, targets);
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
