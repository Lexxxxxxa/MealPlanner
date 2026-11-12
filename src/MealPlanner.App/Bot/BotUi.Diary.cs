using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MealPlanner.App.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Parsing;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.App.Bot;

public partial class BotUi
{
    private static readonly int[] GoalPresets = { 1500, 1800, 2000, 2200, 2500, 3000 };
    private static readonly double[] EatPortions = { 0.5, 1, 1.5, 2, 3 };

    private static string PortionText(double x) =>
        x == 0.5 ? "½" : x == 1.5 ? "1½" : x.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Inv(double x) => x.ToString(CultureInfo.InvariantCulture);

    private static void StartEditing(Session s, HouseholdDish entity, Dish dish, CookMode mode)
    {
        s.Mode = mode;
        s.EditDishId = entity.Id;
        s.EditName = dish.Name;
        s.EditServings = dish.Servings;
        s.EditMinutes = dish.CookMinutes;
        s.EditDifficulty = dish.Difficulty;
        s.Items.Clear();
        s.Recipe.Clear();
        foreach (var ing in dish.Ingredients)
        {
            s.Items.Add((ing.Food.Id, ing.Grams));
            s.Recipe[ing.Food.Id] = ing.Grams;
        }
        s.Step = Step.None;
    }

    private static Nutrients Sum(IEnumerable<(int FoodId, double Grams)> items, IReadOnlyDictionary<int, Food> map) =>
        items.Aggregate(Nutrients.Zero, (acc, i) => acc + map[i.FoodId].NutrientsFor(i.Grams));

    private static BotReply EditScreen(Session s, IReadOnlyList<Food> foods, string? note)
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
                var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
                var entity = await _db.Dishes.Include(d => d.Ingredients)
                    .FirstOrDefaultAsync(d => d.Id == s.EditDishId && d.HouseholdId == member.HouseholdId, ct);
                if (entity is null) return await DishListAsync(userId, userName, ct);

                entity.Servings = s.EditServings;
                entity.CookMinutes = s.EditMinutes;
                entity.Difficulty = s.EditDifficulty;
                _db.DishIngredients.RemoveRange(entity.Ingredients);
                entity.Ingredients = s.Items.GroupBy(i => i.FoodId)
                    .Select(g => new HouseholdDishIngredient { DishId = entity.Id, FoodId = g.Key, Grams = g.Sum(x => x.Grams) })
                    .ToList();
                await _db.SaveChangesAsync(ct);

                int id = entity.Id;
                s.EditDishId = 0; s.Items.Clear(); s.Recipe.Clear();
                var dish = new Dish(entity.Name, entity.Servings,
                    entity.Ingredients.Select(i => new DishIngredient(map[i.FoodId], i.Grams)).ToList());
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
                var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
                var perPortion = Sum(s.Items, map) * (1.0 / s.EditServings);
                await AddDiaryAsync(member, s, $"{s.EditName} × {PortionText(portions)} (по факту)", perPortion * portions, ct);
                s.DiaryOffset = 0;
                return await DayAsync(userId, userName, s, ct);
            }
            case "cancel":
            {
                int id = s.EditDishId;
                s.EditDishId = 0; s.Items.Clear(); s.Recipe.Clear(); s.Step = Step.None;
                return await DishCallbackAsync(userId, userName, s, new[] { "d", "v", id.ToString(CultureInfo.InvariantCulture) }, ct);
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

    private async Task<BotReply> TypedNumberAsync(long userId, string userName, Session s, string text, CancellationToken ct)
    {
        var step = s.Step;
        var foods = await FoodsAsync(ct);
        var m = Regex.Match(text.Trim(), @"^(?<num>\d+(?:[.,]\d+)?)\s*(?<unit>[а-яa-z]*)\.?$", RegexOptions.IgnoreCase);
        double? qty = m.Success
            ? double.Parse(m.Groups["num"].Value.Replace(',', '.'), CultureInfo.InvariantCulture)
            : null;
        string unit = m.Success ? m.Groups["unit"].Value.ToLowerInvariant() : "";

        if (step == Step.GoalText)
        {
            if (qty is null || qty < 800 || qty > 6000)
                return new BotReply("Напишите число калорий в день от 800 до 6000, например: 2100");
            s.Step = Step.None;
            await SetTargetAsync(await _legacy.EnsureMemberAsync(userId, userName, ct), qty.Value, ct);
            return await DayAsync(userId, userName, s, ct);
        }

        int foodId = step switch
        {
            Step.EditGrams => s.EditIndex >= 0 && s.EditIndex < s.Items.Count ? s.Items[s.EditIndex].FoodId : 0,
            Step.EditAddGrams => s.EditPendingFoodId,
            _ => s.LogFoodId
        };
        var food = foods.FirstOrDefault(f => f.Id == foodId);
        if (food is null) { s.Step = Step.None; return new BotReply("Что-то пошло не так. Выберите действие в меню внизу."); }

        double? grams = qty is null ? null : DishParser.ToGrams(qty.Value, unit, food);
        if (grams is null || grams <= 0 || grams > 10000)
            return new BotReply("Не поняла количество. Напишите, например: 120, 0,5 кг или 3 шт");

        s.Step = Step.None;
        switch (step)
        {
            case Step.EditGrams:
                s.Items[s.EditIndex] = (food.Id, grams.Value);
                return IngredientScreen(s, foods, s.EditIndex);
            case Step.EditAddGrams:
                AddOrMerge(s, food.Id, grams.Value);
                return EditScreen(s, foods, $"✔️ Добавлено: {food.Name} {grams.Value:F0} г");
            default:
                var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
                await AddDiaryAsync(member, s, $"{food.Name} {grams.Value:F0} г", food.NutrientsFor(grams.Value), ct);
                s.DiaryOffset = 0;
                return await DayAsync(userId, userName, s, ct);
        }
    }

    private async Task AddDiaryAsync(Member member, Session s, string label, Nutrients n, CancellationToken ct)
    {
        _db.DiaryEntries.Add(new DiaryEntry
        {
            MemberId = member.Id,
            Date = _today(),
            Label = label,
            Kcal = n.Kcal, Protein = n.Protein, Fat = n.Fat, Carbs = n.Carbs
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task SetTargetAsync(Member member, double kcal, CancellationToken ct)
    {
        var t = await _db.MemberTargets.FirstOrDefaultAsync(x => x.MemberId == member.Id, ct);
        if (t is null) { t = new MemberTarget { MemberId = member.Id }; _db.MemberTargets.Add(t); }
        t.Kcal = kcal;
        t.IsManual = true;
        await _db.SaveChangesAsync(ct);
    }

    private static string Bar(double value, double target)
    {
        double share = target <= 0 ? 0 : value / target;
        int filled = (int)Math.Round(Math.Min(1, share) * 10);
        return new string('█', filled) + new string('░', 10 - filled) + $" {share * 100:F0}%";
    }

    private string DayTitle(int offset)
    {
        var d = _today().AddDays(offset);
        string date = d.ToString("dd.MM", CultureInfo.InvariantCulture);
        return offset == 0 ? $"Сегодня, {date}" : offset == -1 ? $"Вчера, {date}" : d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }

    private async Task<BotReply> DayAsync(long userId, string userName, Session s, CancellationToken ct)
    {
        var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
        var date = _today().AddDays(s.DiaryOffset);
        var entries = await _db.DiaryEntries.AsNoTracking()
            .Where(e => e.MemberId == member.Id && e.Date == date).OrderBy(e => e.Id).ToListAsync(ct);
        var prof = await ProfileAsync(member.Id, ct);
        double target = prof?.Kcal ?? 2000;
        var (ps, fs, cs) = prof is { HasProfile: true, IsManual: false }
            ? EnergyCalculator.MacroShares(prof.Goal) : (0.20, 0.30, 0.50);

        double kcal = entries.Sum(e => e.Kcal), prot = entries.Sum(e => e.Protein),
               fat = entries.Sum(e => e.Fat), carb = entries.Sum(e => e.Carbs);

        var sb = new StringBuilder($"📅 {DayTitle(s.DiaryOffset)}\n\n");
        if (entries.Count == 0) sb.AppendLine("Пока ничего не записано.");
        for (int i = 0; i < entries.Count; i++) sb.AppendLine($"{i + 1}. {entries[i].Label} — {entries[i].Kcal:F0} ккал");

        sb.AppendLine($"\n🔥 Калории: {kcal:F0} из {target:F0}");
        if (prof is { HasProfile: true }) sb.AppendLine($"🎯 {EnergyCalculator.GoalLabel(prof.Goal)}");
        sb.AppendLine(Bar(kcal, target));
        sb.AppendLine(kcal <= target ? $"Осталось: {target - kcal:F0} ккал" : $"Перебор: +{kcal - target:F0} ккал");
        sb.AppendLine($"\nБелки: {prot:F0} из {target * ps / 4:F0} г");
        sb.AppendLine($"Жиры: {fat:F0} из {target * fs / 9:F0} г");
        sb.Append($"Углеводы: {carb:F0} из {target * cs / 4:F0} г");

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
        var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
        if (p.Length > 1 && p[1] is "fit" or "fv" or "fa")
            return await FitCallbackAsync(userId, userName, s, p, ct);
        switch (p.Length > 1 ? p[1] : "")
        {
            case "prev": s.DiaryOffset--; break;
            case "next": s.DiaryOffset = Math.Min(0, s.DiaryOffset + 1); break;
            case "today": s.DiaryOffset = 0; break;
            case "del":
            {
                int id = int.Parse(p[2], CultureInfo.InvariantCulture);
                var e = await _db.DiaryEntries.FirstOrDefaultAsync(x => x.Id == id && x.MemberId == member.Id, ct);
                if (e != null) { _db.DiaryEntries.Remove(e); await _db.SaveChangesAsync(ct); }
                break;
            }
            case "dish":
            {
                var foods = await FoodsAsync(ct);
                var dishes = await _legacy.LoadDishesAsync(member.HouseholdId, foods, ct);
                if (dishes.Count == 0)
                    return new BotReply("У вас пока нет сохранённых блюд. Добавьте их в «➕ Добавить блюдо» или запишите продукт.",
                        new[] { Row(new InlineButton("➕ Продукт", "l:home"), new InlineButton("⬅️ Назад", "t:day")) });
                var rows = Rows(dishes.Select(d => new InlineButton($"{d.Model.Name} · {d.Model.PerServing.Kcal:F0} ккал", $"d:eat:{d.Entity.Id}")), 1);
                rows.Add(Row(new InlineButton("⬅️ Назад", "t:day")));
                return new BotReply("Что вы съели?", rows);
            }
            case "goal":
                return await GoalHomeAsync(member, ct);
            case "g":
                await SetTargetAsync(member, double.Parse(p[2], CultureInfo.InvariantCulture), ct);
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
                var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
                await AddDiaryAsync(member, s, $"{map[id].Name} {g:F0} г", map[id].NutrientsFor(g), ct);
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
