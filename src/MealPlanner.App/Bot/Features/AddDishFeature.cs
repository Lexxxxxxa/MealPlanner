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
/// Add dish flow: name, products, grams, time, difficulty, recipe and saving
/// </summary>
internal sealed class AddDishFeature : FeatureBase
{
    public AddDishFeature(BotContext ctx) : base(ctx)
    {
    }

    public override IReadOnlyCollection<string> Prefixes { get; } = new[] { "a" };

    public override IReadOnlyCollection<Step> Steps { get; } =
        new[] { Step.AddName, Step.AddCustomGrams, Step.NewFoodValues, Step.AddInstructions };

    internal BotReply Start(Session session)
    {
        session.ResetDraft();
        session.Step = Step.AddName;
        return new BotReply("Как называется блюдо? Напишите название, например: Борщ");
    }

    public override Task<BotReply> HandleCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct) =>
        AddCallbackAsync(userId, userName, s, p, ct);

    public override async Task<BotReply> HandleStepAsync(long userId, string userName, Session s, string text, CancellationToken ct)
    {
        switch (s.Step)
        {
            case Step.AddName:
                if (text.Length == 0 || text.Length > 60) return new BotReply("Название должно быть от 1 до 60 символов. Попробуйте ещё раз.");
                s.DraftName = text.Replace(":", " ").Trim();
                s.Draft.Clear();
                s.Step = Step.None;
                return await AddHomeAsync(s, null, ct);
            case Step.AddCustomGrams: return await CustomGramsAsync(s, text, ct);
            case Step.NewFoodValues: return await CreateFoodAsync(s, text, ct);
            default: return await FinishDraftAsync(userId, userName, s, InstructionText.Normalize(text), ct);
        }
    }

    private async Task<BotReply> FinishDraftAsync(long userId, string userName, Session s, string instructions, CancellationToken ct)
    {
        var foods = (await FoodsAsync(ct)).ToDictionary(f => f.Id);
        var ingredients = s.Draft.GroupBy(d => d.FoodId)
            .Select(g => new DishIngredient(foods[g.Key], g.Sum(x => x.Grams))).ToList();
        var dish = new Dish(s.DraftName, s.DraftServings, ingredients, s.DraftMinutes, s.DraftDifficulty, instructions);

        var member = await MemberAsync(userId, userName, ct);
        await Services.Dishes.SaveAsync(member.HouseholdId, dish, ct);
        s.ResetDraft();
        s.Step = Step.None;

        string recipe = dish.Steps.Count > 0 ? $"\n📝 Рецепт: {dish.Steps.Count} шагов (виден в карточке блюда)" : "\n📝 Рецепт не добавлен — его можно дописать в карточке блюда.";
        return new BotReply(
            $"✅ Блюдо «{dish.Name}» сохранено!\n{TimeInfo(dish)}{recipe}\n\nНа порцию: {Nutr(dish.PerServing)}\nВсего ({dish.Servings} порц.): {Nutr(dish.Total)}",
            new[]
            {
                Row(new InlineButton("➕ Ещё блюдо", "a:again"), new InlineButton("📖 Мои блюда", "d:list"))
            });
    }

    private async Task<BotReply> AddHomeAsync(Session s, string? note, CancellationToken ct)
    {
        var foods = await FoodsAsync(ct);
        return AddHome(s, foods, note);
    }

    private static BotReply InstructionsPrompt(Session s)
    {
        s.Step = Step.AddInstructions;
        return new BotReply(
            $"📝 Как готовить «{s.DraftName}»?\n\nНапишите шаги рецепта: каждый шаг с новой строки (нумерация не нужна).\nМожно пропустить и дописать позже.",
            new[] { Row(new InlineButton("⏭ Пропустить", "a:skip"), new InlineButton("⬅️ Назад", $"a:tm:{s.DraftMinutes}")) });
    }

    private static BotReply AddHome(Session s, IReadOnlyList<Food> foods, string? note)
    {
        var map = foods.ToDictionary(f => f.Id);
        var sb = new StringBuilder($"➕ Блюдо «{s.DraftName}»\n");
        if (note != null) sb.AppendLine(note);
        if (s.Draft.Count == 0)
        {
            sb.AppendLine("\nДобавьте продукты: откройте категорию и нажмите на продукт.");
        }
        else
        {
            sb.AppendLine("\nСостав:");
            foreach (var (foodId, grams) in s.Draft) sb.AppendLine($"• {map[foodId].Name} — {grams:F0} г");
            double kcal = s.Draft.Sum(d => map[d.FoodId].NutrientsFor(d.Grams).Kcal);
            sb.AppendLine($"\nСейчас ≈ {kcal:F0} ккал всего");
        }

        var rows = Rows(Categories(foods).Select((c, i) => new InlineButton(CatLabel(c), $"a:c:{i}")), 2);
        rows.Add(Row(new InlineButton("🔎 Найти продукт", "a:find"), new InlineButton("↩️ Убрать последний", "a:undo")));
        rows.Add(Row(new InlineButton("✖️ Отмена", "a:cancel"), new InlineButton("✅ Готово", "a:done")));
        return new BotReply(sb.ToString().TrimEnd(), rows);
    }

    private static BotReply AddCategory(Session s, IReadOnlyList<Food> foods, int ci, int page = 0)
    {
        var cats = Categories(foods);
        if (ci < 0 || ci >= cats.Count) return AddHome(s, foods, null);
        var list = foods.Where(f => f.Category == cats[ci]).OrderBy(f => f.Name).ToList();
        return FoodPage($"{CatLabel(cats[ci])} ({list.Count})\nВыберите продукт:", list, page,
            (f, _) => $"a:p:{f.Id}", pg => $"a:c:{ci}:{pg}",
            Row(new InlineButton("⬅️ Назад", "a:home"), new InlineButton("🔎 Найти", "a:find")));
    }

    private static BotReply GramsScreen(Food food)
    {
        var rows = Rows(GramPresets.Select(g => new InlineButton($"{g} г", $"a:g:{food.Id}:{g}")), 4);
        rows.Add(Row(new InlineButton("✍️ Другое число", $"a:gx:{food.Id}"), new InlineButton("⬅️ Назад", "a:home")));
        return new BotReply($"Сколько продукта «{food.Name}» в блюде?\nВыберите граммы:", rows);
    }

    private async Task<BotReply> AddCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct)
    {
        var foods = await FoodsAsync(ct);
        string action = p.Length > 1 ? p[1] : "";

        if (action != "cancel" && string.IsNullOrEmpty(s.DraftName))
            return new BotReply("Начните заново: нажмите «➕ Добавить блюдо» внизу.");

        switch (action)
        {
            case "home": s.Step = Step.None; return AddHome(s, foods, null);
            case "c": return AddCategory(s, foods, int.Parse(p[2], CultureInfo.InvariantCulture), PageArg(p, 3));
            case "q": return SearchReply(s, foods, PageArg(p, 2));
            case "p":
            {
                var food = foods.FirstOrDefault(f => f.Id == int.Parse(p[2], CultureInfo.InvariantCulture));
                return food is null ? AddHome(s, foods, null) : GramsScreen(food);
            }
            case "g":
            {
                int foodId = int.Parse(p[2], CultureInfo.InvariantCulture);
                double grams = double.Parse(p[3], CultureInfo.InvariantCulture);
                s.Draft.Add((foodId, grams));
                string name = foods.First(f => f.Id == foodId).Name;
                return AddHome(s, foods, $"✔️ Добавлено: {name} {grams:F0} г");
            }
            case "gx":
                s.PendingFoodId = int.Parse(p[2], CultureInfo.InvariantCulture);
                s.Step = Step.AddCustomGrams;
                return new BotReply("Напишите количество, например: 120, 0,5 кг или 3 шт");
            case "undo":
                if (s.Draft.Count > 0) s.Draft.RemoveAt(s.Draft.Count - 1);
                return AddHome(s, foods, null);
            case "find":
                s.SearchCtx = 'a';
                s.Step = Step.SearchFood;
                return new BotReply("Напишите название продукта (или его часть), например: сыр");
            case "new":
                s.Step = Step.NewFoodValues;
                return new BotReply(
                    $"Добавим «{s.NewFoodName}». Напишите через пробел на 100 г:\n" +
                    "калории, белки, жиры, углеводы (цена за 100 г — по желанию).\n" +
                    "Например: 264 14 21 4");
            case "cancel":
                s.ResetDraft();
                s.Step = Step.None;
                return new BotReply("Отменено. Выберите действие кнопкой внизу.");
            case "done":
                if (s.Draft.Count == 0) return AddHome(s, foods, "⚠️ Добавьте хотя бы один продукт.");
                return new BotReply($"На сколько порций блюдо «{s.DraftName}»?",
                    Rows(ServingPresets.Select(n => new InlineButton(n.ToString(), $"a:s:{n}")), 4)
                        .Append(Row(new InlineButton("⬅️ Назад", "a:home"))).ToList());
            case "s":
            {
                if (s.Draft.Count == 0) return AddHome(s, foods, null);
                s.DraftServings = int.Parse(p[2], CultureInfo.InvariantCulture);
                return TimeScreen($"Сколько времени готовится «{s.DraftName}»?", "a:tm", "a:done");
            }
            case "tm":
                s.DraftMinutes = int.Parse(p[2], CultureInfo.InvariantCulture);
                return DifficultyScreen("Насколько сложно готовить это блюдо?", "a:df", $"a:s:{s.DraftServings}");
            case "df":
                if (s.Draft.Count == 0) return AddHome(s, foods, null);
                s.DraftDifficulty = int.Parse(p[2], CultureInfo.InvariantCulture);
                return InstructionsPrompt(s);
            case "skip":
                if (s.Draft.Count == 0) return AddHome(s, foods, null);
                return await FinishDraftAsync(userId, userName, s, string.Empty, ct);
            case "again":
                s.ResetDraft();
                s.Step = Step.AddName;
                return new BotReply("Как называется следующее блюдо?");
            default: return AddHome(s, foods, null);
        }
    }

    private async Task<BotReply> CustomGramsAsync(Session s, string text, CancellationToken ct)
    {
        var foods = await FoodsAsync(ct);
        var food = foods.FirstOrDefault(f => f.Id == s.PendingFoodId);
        if (food is null) { s.Step = Step.None; return AddHome(s, foods, null); }

        var m = Regex.Match(text.Trim(), @"^(?<num>\d+(?:[.,]\d+)?)\s*(?<unit>[а-яa-z]*)\.?$", RegexOptions.IgnoreCase);
        double? grams = null;
        if (m.Success)
        {
            double qty = double.Parse(m.Groups["num"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            grams = DishParser.ToGrams(qty, m.Groups["unit"].Value.ToLowerInvariant(), food);
        }
        if (grams is null || grams <= 0 || grams > 10000)
            return new BotReply("Не поняла количество. Напишите, например: 120, 0,5 кг или 3 шт");

        s.Step = Step.None;
        s.Draft.Add((food.Id, grams.Value));
        return AddHome(s, foods, $"✔️ Добавлено: {food.Name} {grams.Value:F0} г");
    }

    private async Task<BotReply> CreateFoodAsync(Session s, string text, CancellationToken ct)
    {
        var nums = Regex.Matches(text, @"\d+(?:[.,]\d+)?")
            .Select(m => double.Parse(m.Value.Replace(',', '.'), CultureInfo.InvariantCulture)).ToList();
        if (nums.Count < 4 || nums.Count > 5)
            return new BotReply("Нужно 4 числа через пробел: калории, белки, жиры, углеводы. Например: 264 14 21 4");

        var food = new Food
        {
            Name = s.NewFoodName,
            Category = FoodRepository.CustomCategory,
            KcalPer100 = nums[0], ProteinPer100 = nums[1], FatPer100 = nums[2], CarbsPer100 = nums[3],
            PricePer100 = nums.Count == 5 ? nums[4] : FoodRepository.DefaultPrice,
            MaxGrams = FoodRepository.DefaultMaxGrams
        };
        await Services.Foods.AddAsync(food, ct);

        s.Step = Step.None;
        s.PendingFoodId = food.Id;
        return GramsScreen(food);
    }
}
