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
/// Recipe editing of a saved dish and cook mode with actual grams
/// </summary>
internal sealed class EditFeature : FeatureBase
{
    public EditFeature(BotContext ctx) : base(ctx)
    {
    }

    public override IReadOnlyCollection<string> Prefixes { get; } = new[] { "e" };

    public override IReadOnlyCollection<Step> Steps { get; } = new[] { Step.EditGrams, Step.EditAddGrams };

    public override Task<BotReply> HandleCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct) =>
        EditCallbackAsync(userId, userName, s, p, ct);

    public override async Task<BotReply> HandleStepAsync(long userId, string userName, Session s, string text, CancellationToken ct)
    {
        var step = s.Step;
        var foods = await FoodsAsync(ct);
        var (qty, unit) = ParseQuantity(text);

        int foodId = step == Step.EditGrams
            ? (s.EditIndex >= 0 && s.EditIndex < s.Items.Count ? s.Items[s.EditIndex].FoodId : 0)
            : s.EditPendingFoodId;
        var food = foods.FirstOrDefault(f => f.Id == foodId);
        if (food is null) { s.Step = Step.None; return new BotReply("Что-то пошло не так. Выберите действие в меню внизу."); }

        double? grams = qty is null ? null : DishParser.ToGrams(qty.Value, unit, food);
        if (grams is null || grams <= 0 || grams > 10000)
            return new BotReply("Не поняла количество. Напишите, например: 120, 0,5 кг или 3 шт");

        s.Step = Step.None;
        if (step == Step.EditGrams)
        {
            s.Items[s.EditIndex] = (food.Id, grams.Value);
            return IngredientScreen(s, foods, s.EditIndex);
        }

        AddOrMerge(s, food.Id, grams.Value);
        return EditScreen(s, foods, $"✔️ Добавлено: {food.Name} {grams.Value:F0} г");
    }

    internal static void StartEditing(Session s, HouseholdDish entity, Dish dish, CookMode mode)
    {
        s.Mode = mode;
        s.EditDishId = entity.Id;
        s.EditName = dish.Name;
        s.EditServings = dish.Servings;
        s.EditMinutes = dish.CookMinutes;
        s.EditDifficulty = dish.Difficulty;
        s.EditInstructions = dish.Instructions;
        s.Items.Clear();
        s.Recipe.Clear();
        foreach (var ing in dish.Ingredients)
        {
            s.Items.Add((ing.Food.Id, ing.Grams));
            s.Recipe[ing.Food.Id] = ing.Grams;
        }
        s.Step = Step.None;
    }

    internal static BotReply EditScreen(Session s, IReadOnlyList<Food> foods, string? note)
    {
        var map = foods.ToDictionary(f => f.Id);
        bool cook = s.Mode == CookMode.Cook;
        var sb = new StringBuilder(cook
            ? $"👩‍🍳 Готовлю «{s.EditName}»\nУкажите, сколько продуктов реально положили — калории пересчитаются.\n"
            : $"✏️ Изменение блюда «{s.EditName}»\n");
        if (note != null) sb.AppendLine(note);

        var rows = new List<IReadOnlyList<InlineButton>>();
        sb.AppendLine();
        for (int i = 0; i < s.Items.Count; i++)
        {
            var (foodId, grams) = s.Items[i];
            string name = map[foodId].Name;
            string extra = "";
            if (cook)
            {
                if (s.Recipe.TryGetValue(foodId, out double r))
                    extra = Math.Abs(r - grams) < 0.5 ? "" : $" (по рецепту {r:F0})";
                else extra = " (новое)";
            }
            sb.AppendLine($"• {name} — {grams:F0} г{extra}");
            rows.Add(Row(new InlineButton($"✏️ {name} · {grams:F0} г", $"e:i:{i}")));
        }

        var actual = Sum(s.Items, map);
        sb.AppendLine();
        if (cook)
        {
            var recipe = Sum(s.Recipe.Select(kv => (kv.Key, kv.Value)), map);
            sb.AppendLine($"Фактически всего: {Nutr(actual)}");
            sb.AppendLine($"По рецепту всего: {Nutr(recipe)}");
            sb.AppendLine($"\nПорций получилось: {s.EditServings}");
            sb.AppendLine($"На порцию фактически: {Nutr(actual * (1.0 / s.EditServings))}");
            sb.Append($"Разница с рецептом: {(actual.Kcal - recipe.Kcal) / s.EditServings:+0;-0;0} ккал на порцию");
            var steps = InstructionText.Split(s.EditInstructions);
            if (steps.Count > 0) sb.Append("\n\n📝 Как готовить:\n" + InstructionText.Format(steps));
        }
        else
        {
            sb.AppendLine($"Порций: {s.EditServings}");
            sb.Append($"На порцию: {Nutr(actual * (1.0 / s.EditServings))}");
        }

        rows.Add(Row(new InlineButton("➕ Добавить продукт", "e:add"), new InlineButton("🔢 Порции", "e:sv")));
        rows.Add(Row(new InlineButton($"⏱ {s.EditMinutes} мин", "e:tm"), new InlineButton($"🔧 {DifficultyText(s.EditDifficulty)}", "e:df")));
        if (cook)
        {
            rows.Add(Row(new InlineButton("🍽 Съел(а) порцию", "e:eat"), new InlineButton("💾 Обновить рецепт", "e:save")));
            rows.Add(Row(new InlineButton("✖️ Закрыть", "e:cancel")));
        }
        else
        {
            rows.Add(Row(new InlineButton("💾 Сохранить", "e:save"), new InlineButton("✖️ Отмена", "e:cancel")));
        }
        return new BotReply(sb.ToString(), rows);
    }

    private BotReply IngredientScreen(Session s, IReadOnlyList<Food> foods, int idx)
    {
        if (idx < 0 || idx >= s.Items.Count) return EditScreen(s, foods, null);
        var (foodId, grams) = s.Items[idx];
        var food = foods.First(f => f.Id == foodId);
        string recipe = s.Mode == CookMode.Cook && s.Recipe.TryGetValue(foodId, out double r) ? $"\nПо рецепту: {r:F0} г" : "";
        var n = food.NutrientsFor(grams);
        var rows = new List<IReadOnlyList<InlineButton>>
        {
            Row(new InlineButton("−50", $"e:a:{idx}:-50"), new InlineButton("−10", $"e:a:{idx}:-10"),
                new InlineButton("+10", $"e:a:{idx}:10"), new InlineButton("+50", $"e:a:{idx}:50")),
            Row(new InlineButton("✍️ Ввести граммы", $"e:x:{idx}"), new InlineButton("🗑 Убрать", $"e:rm:{idx}")),
            Row(new InlineButton("⬅️ Готово", "e:home"))
        };
        return new BotReply($"{food.Name}: {grams:F0} г{recipe}\n{Nutr(n)}", rows);
    }

    private async Task<BotReply> EditCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct)
    {
        if (s.EditDishId == 0 || s.Items.Count == 0 && p[1] != "add" && p[1] != "c" && p[1] != "p" && p[1] != "g" && p[1] != "gx")
            return new BotReply("Откройте блюдо в «📖 Мои блюда» и нажмите «Изменить» или «Готовлю».");

        var foods = await FoodsAsync(ct);
        var map = foods.ToDictionary(f => f.Id);
        string action = p[1];

        switch (action)
        {
            case "home": s.Step = Step.None; return EditScreen(s, foods, null);
            case "i": s.EditIndex = int.Parse(p[2], CultureInfo.InvariantCulture); return IngredientScreen(s, foods, s.EditIndex);
            case "a":
            {
                int idx = int.Parse(p[2], CultureInfo.InvariantCulture);
                double delta = double.Parse(p[3], CultureInfo.InvariantCulture);
                if (idx >= 0 && idx < s.Items.Count)
                    s.Items[idx] = (s.Items[idx].FoodId, Math.Max(5, s.Items[idx].Grams + delta));
                return IngredientScreen(s, foods, idx);
            }
            case "x":
                s.EditIndex = int.Parse(p[2], CultureInfo.InvariantCulture);
                s.Step = Step.EditGrams;
                return new BotReply("Напишите, сколько граммов, например: 250 или 0,3 кг");
            case "rm":
            {
                int idx = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (s.Items.Count <= 1) return EditScreen(s, foods, "⚠️ В блюде должен остаться хотя бы один продукт.");
                if (idx >= 0 && idx < s.Items.Count) s.Items.RemoveAt(idx);
                return EditScreen(s, foods, "🗑 Продукт убран");
            }
            case "add":
            {
                var cats = Categories(foods);
                var rows = Rows(cats.Select((c, i) => new InlineButton(CatLabel(c), $"e:c:{i}")), 2);
                rows.Add(Row(new InlineButton("🔎 Найти продукт", "e:find"), new InlineButton("⬅️ Назад", "e:home")));
                return new BotReply("Какой продукт добавить?", rows);
            }
            case "c":
            {
                var cats = Categories(foods);
                int ci = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (ci < 0 || ci >= cats.Count) return EditScreen(s, foods, null);
                var list = foods.Where(f => f.Category == cats[ci]).OrderBy(f => f.Name).ToList();
                return FoodPage($"{CatLabel(cats[ci])} ({list.Count})\nВыберите продукт:", list, PageArg(p, 3),
                    (f, _) => $"e:p:{f.Id}", pg => $"e:c:{ci}:{pg}",
                    Row(new InlineButton("⬅️ Назад", "e:add"), new InlineButton("🔎 Найти", "e:find")));
            }
            case "find":
                s.SearchCtx = 'e';
                s.Step = Step.SearchFood;
                return new BotReply("Напишите название продукта или его часть, например: сыр");
            case "q": return SearchReply(s, foods, PageArg(p, 2));
            case "p":
            {
                int id = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (!map.TryGetValue(id, out var food)) return EditScreen(s, foods, null);
                var rows = Rows(GramPresets.Select(g => new InlineButton($"{g} г", $"e:g:{id}:{g}")), 4);
                rows.Add(Row(new InlineButton("✍️ Другое число", $"e:gx:{id}"), new InlineButton("⬅️ Назад", "e:add")));
                return new BotReply($"Сколько «{food.Name}» добавить?", rows);
            }
            case "g":
            {
                int id = int.Parse(p[2], CultureInfo.InvariantCulture);
                double g = double.Parse(p[3], CultureInfo.InvariantCulture);
                AddOrMerge(s, id, g);
                return EditScreen(s, foods, $"✔️ Добавлено: {map[id].Name} {g:F0} г");
            }
            case "gx":
                s.EditPendingFoodId = int.Parse(p[2], CultureInfo.InvariantCulture);
                s.Step = Step.EditAddGrams;
                return new BotReply("Напишите количество, например: 120, 0,5 кг или 3 шт");
            case "tm":
                return TimeScreen("Сколько времени готовится блюдо?", "e:tmv", "e:home");
            case "tmv":
                s.EditMinutes = int.Parse(p[2], CultureInfo.InvariantCulture);
                return EditScreen(s, foods, null);
            case "df":
                return DifficultyScreen("Насколько сложно готовить это блюдо?", "e:dfv", "e:home");
            case "dfv":
                s.EditDifficulty = int.Parse(p[2], CultureInfo.InvariantCulture);
                return EditScreen(s, foods, null);
            case "sv":
                return new BotReply("Сколько порций получилось?",
                    Rows(ServingPresets.Select(n => new InlineButton(n.ToString(), $"e:s:{n}")), 4)
                        .Append(Row(new InlineButton("⬅️ Назад", "e:home"))).ToList());
            case "s":
                s.EditServings = Math.Max(1, int.Parse(p[2], CultureInfo.InvariantCulture));
                return EditScreen(s, foods, null);
            case "save":
            {
                var member = await MemberAsync(userId, userName, ct);
                var ingredients = s.Items.GroupBy(i => i.FoodId)
                    .Select(g => new DishIngredient(map[g.Key], g.Sum(x => x.Grams))).ToList();
                var dish = new Dish(s.EditName, s.EditServings, ingredients, s.EditMinutes, s.EditDifficulty, s.EditInstructions);
                if (!await Services.Dishes.UpdateAsync(member.HouseholdId, s.EditDishId, dish, ct))
                    return await Ctx.Features.Dishes.DishListAsync(userId, userName, ct);

                int id = s.EditDishId;
                s.EditDishId = 0; s.Items.Clear(); s.Recipe.Clear();
                return new BotReply($"✅ Рецепт «{dish.Name}» обновлён.\nНа порцию: {Nutr(dish.PerServing)}",
                    new[] { Row(new InlineButton("📖 К блюду", $"d:v:{id}")) });
            }
            case "eat":
            {
                var perPortion = Sum(s.Items, map) * (1.0 / s.EditServings);
                return new BotReply($"Сколько порций «{s.EditName}» вы съели?\nНа порцию (по факту): {Nutr(perPortion)}",
                    Rows(EatPortions.Select(x => new InlineButton(PortionText(x), $"e:ate:{Inv(x)}")), 3)
                        .Append(Row(new InlineButton("⬅️ Назад", "e:home"))).ToList());
            }
            case "ate":
            {
                double portions = double.Parse(p[2], CultureInfo.InvariantCulture);
                var member = await MemberAsync(userId, userName, ct);
                var perPortion = Sum(s.Items, map) * (1.0 / s.EditServings);
                await Services.Diary.AddAsync(member.Id, Ctx.Today(), $"{s.EditName} × {PortionText(portions)} (по факту)", perPortion * portions, ct);
                s.DiaryOffset = 0;
                return await Ctx.Features.Diary.DayAsync(userId, userName, s, ct);
            }
            case "cancel":
            {
                int id = s.EditDishId;
                s.EditDishId = 0; s.Items.Clear(); s.Recipe.Clear(); s.Step = Step.None;
                return await Ctx.Features.Dishes.DishCallbackAsync(userId, userName, s, new[] { "d", "v", id.ToString(CultureInfo.InvariantCulture) }, ct);
            }
            default: return EditScreen(s, foods, null);
        }
    }

    private static void AddOrMerge(Session s, int foodId, double grams)
    {
        int i = s.Items.FindIndex(x => x.FoodId == foodId);
        if (i >= 0) s.Items[i] = (foodId, s.Items[i].Grams + grams);
        else s.Items.Add((foodId, grams));
    }
}
