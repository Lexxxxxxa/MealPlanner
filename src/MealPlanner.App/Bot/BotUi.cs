using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MealPlanner.App.Data;
using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Parsing;
using MealPlanner.Core.Recommendation;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.App.Bot;

/// <summary>
/// Button-driven bot interface: main menu, what to cook, my dishes, add dish, pair
/// Independent of Telegram: takes text or pressed button data and returns a <see cref="BotReply"/>
/// </summary>
public partial class BotUi
{
    public const string KeyIdea = "Что приготовить";
    public const string KeyDishes = "Мои блюда";
    public const string KeyAdd = "Добавить блюдо";
    public const string KeyPair = "Пара";
    public const string KeyHelp = "Помощь";
    public const string KeyDiary = "Дневник";

    public static readonly string[][] MainMenu =
    {
        new[] { "🍳 " + KeyIdea, "📖 " + KeyDishes },
        new[] { "➕ " + KeyAdd, "📅 " + KeyDiary },
        new[] { "👫 " + KeyPair, "❓ " + KeyHelp }
    };

    private static readonly string[] CategoryOrder =
    {
        "Мясо и рыба", "Молочные и яйца", "Овощи", "Крупы и хлеб",
        "Бобовые", "Фрукты", "Жиры и орехи", "Сладкое", "Свои продукты"
    };

    private static readonly Dictionary<string, string> CategoryEmoji = new()
    {
        ["Мясо и рыба"] = "🥩", ["Молочные и яйца"] = "🥛", ["Овощи"] = "🥦", ["Крупы и хлеб"] = "🍚",
        ["Бобовые"] = "🫘", ["Фрукты"] = "🍎", ["Жиры и орехи"] = "🧈", ["Сладкое"] = "🍯", ["Свои продукты"] = "⭐"
    };

    private static readonly int[] GramPresets = { 50, 100, 150, 200, 250, 300, 400, 500 };
    private static readonly int[] ServingPresets = { 1, 2, 3, 4, 5, 6, 8, 10 };

    private readonly AppDbContext _db;
    private readonly CommandHandler _legacy;
    private readonly SessionStore _sessions;
    private readonly Func<DateOnly> _today;

    public BotUi(AppDbContext db, SessionStore sessions, Func<DateOnly>? today = null)
    {
        _db = db;
        _sessions = sessions;
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
        _legacy = new CommandHandler(db);
    }

    public async Task<BotReply> HandleMessageAsync(long userId, string userName, string text, CancellationToken ct = default)
    {
        string t = (text ?? string.Empty).Trim();
        var session = _sessions.Get(userId);

        if (t.StartsWith('/'))
        {
            string cmd = t.Split(' ', '\n')[0].ToLowerInvariant();
            int at = cmd.IndexOf('@');
            if (at > 0) cmd = cmd[..at];

            if (cmd is "/start" or "/menu")
            {
                session.Step = Step.None;
                var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
                return new BotReply($"Привет, {member.Name}! 👋\nЯ помогу решить, что приготовить, и посчитаю калории.\nВыберите действие кнопкой внизу.");
            }
            if (cmd == "/help") return Help();

            return new BotReply(await _legacy.HandleAsync(userId, userName, t, ct));
        }

        if (IsKey(t, KeyIdea)) return await IdeaHomeAsync(userId, userName, reset: true, ct);
        if (IsKey(t, KeyDishes)) { session.Step = Step.None; return await DishListAsync(userId, userName, ct); }
        if (IsKey(t, KeyAdd))
        {
            session.ResetDraft();
            session.Step = Step.AddName;
            return new BotReply("Как называется блюдо? Напишите название, например: Борщ");
        }
        if (IsKey(t, KeyPair)) { session.Step = Step.None; return await PairAsync(userId, userName, ct); }
        if (IsKey(t, KeyDiary)) { session.Step = Step.None; session.DiaryOffset = 0; return await DayAsync(userId, userName, session, ct); }
        if (IsKey(t, KeyHelp)) { session.Step = Step.None; return Help(); }

        switch (session.Step)
        {
            case Step.AddName:
                if (t.Length == 0 || t.Length > 60) return new BotReply("Название должно быть от 1 до 60 символов. Попробуйте ещё раз.");
                session.DraftName = t.Replace(":", " ").Trim();
                session.Draft.Clear();
                session.Step = Step.None;
                return await AddHomeAsync(session, null, ct);

            case Step.AddCustomGrams:
                return await CustomGramsAsync(session, t, ct);

            case Step.AddSearch:
                return await SearchAsync(session, t, ct);

            case Step.NewFoodValues:
                return await CreateFoodAsync(session, t, ct);

            case Step.IdeaText:
                return await IdeaFromTextAsync(userId, userName, session, t, ct);

            case Step.ProfAge:
            case Step.ProfHeight:
            case Step.ProfWeight:
                return ProfileTyped(session, t)!;

            case Step.EditGrams:
            case Step.EditAddGrams:
            case Step.LogGrams:
            case Step.GoalText:
                return await TypedNumberAsync(userId, userName, session, t, ct);

            case Step.JoinCode:
                session.Step = Step.None;
                return new BotReply(await _legacy.JoinAsync(userId, userName, t, ct));
        }

        if (t.Contains(':'))
            return new BotReply(await _legacy.HandleAsync(userId, userName, t, ct));

        return new BotReply("Выберите действие кнопкой внизу 👇");
    }

    private static bool IsKey(string text, string key) =>
        text.Length <= key.Length + 4 && text.EndsWith(key, StringComparison.OrdinalIgnoreCase);

    public async Task<BotReply> HandleCallbackAsync(long userId, string userName, string data, CancellationToken ct = default)
    {
        var s = _sessions.Get(userId);
        var p = data.Split(':');

        switch (p[0])
        {
            case "i": return await IdeaCallbackAsync(userId, userName, s, p, ct);
            case "a": return await AddCallbackAsync(userId, userName, s, p, ct);
            case "d": return await DishCallbackAsync(userId, userName, s, p, ct);
            case "r": return await ResultCallbackAsync(userId, userName, s, p, ct);
            case "e": return await EditCallbackAsync(userId, userName, s, p, ct);
            case "t": return await DiaryCallbackAsync(userId, userName, s, p, ct);
            case "l": return await LogCallbackAsync(userId, userName, s, p, ct);
            case "g": return await ProfileCallbackAsync(userId, userName, s, p, ct);
            case "p":
                s.Step = Step.JoinCode;
                return new BotReply("Введите код партнёра (6 символов, например ABC123):");
            default:
                return new BotReply("Не понял кнопку. Выберите действие в меню внизу.");
        }
    }

    private static BotReply Help() => new(
        "Как пользоваться:\n\n" +
        "🍳 Что приготовить — отметьте продукты, которые есть сейчас, и я подскажу блюда из вашей базы.\n" +
        "📖 Мои блюда — все блюда с калориями.\n" +
        "➕ Добавить блюдо — выберите продукты кнопками и граммы, калории посчитаются сами.\n" +
        "✏️ В блюде можно изменить продукты и граммы, а «Готовлю» пересчитает калории по тому, что вы реально положили.\n" +
        "📅 Дневник — что вы съели за день: калории и БЖВ. Норма считается по вашему ИМТ, активности и цели (похудеть, держать вес или набрать).\n" +
        "👫 Пара — общая база блюд для двоих.");

    private async Task<List<Food>> FoodsAsync(CancellationToken ct) =>
        await _db.Foods.AsNoTracking().ToListAsync(ct);

    private static List<string> Categories(IEnumerable<Food> foods) =>
        foods.Select(f => f.Category).Distinct()
            .OrderBy(c => { int i = Array.IndexOf(CategoryOrder, c); return i < 0 ? 99 : i; })
            .ToList();

    private static string CatLabel(string cat) =>
        (CategoryEmoji.TryGetValue(cat, out var e) ? e : "🍽") + " " + cat;

    private static IReadOnlyList<IReadOnlyList<InlineButton>> Grid(IEnumerable<InlineButton> buttons, int perRow) =>
        buttons.Chunk(perRow).Select(c => (IReadOnlyList<InlineButton>)c.ToList()).ToList();

    private static List<IReadOnlyList<InlineButton>> Rows(IEnumerable<InlineButton> buttons, int perRow) =>
        Grid(buttons, perRow).ToList();

    private static IReadOnlyList<InlineButton> Row(params InlineButton[] b) => b.ToList();

    private static string Nutr(Nutrients n) => $"{n.Kcal:F0} ккал · Б {n.Protein:F0} / Ж {n.Fat:F0} / У {n.Carbs:F0}";

    private async Task<BotReply> IdeaHomeAsync(long userId, string userName, bool reset, CancellationToken ct)
    {
        var s = _sessions.Get(userId);
        s.Step = Step.None;
        if (reset) { s.Selected.Clear(); s.Results.Clear(); }
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
        rows.Add(Row(new InlineButton("✍️ Написать списком", "i:txt")));
        rows.Add(FilterRow(s));
        rows.Add(Row(new InlineButton("🗑 Сбросить", "i:reset"),
                     new InlineButton($"✅ Готово ({s.Selected.Count})", "i:done")));
        return new BotReply(sb.ToString(), rows);
    }

    private static BotReply IdeaCategory(Session s, IReadOnlyList<Food> foods, int ci)
    {
        var cats = Categories(foods);
        if (ci < 0 || ci >= cats.Count) return IdeaHome(s, foods);
        string cat = cats[ci];

        var buttons = foods.Where(f => f.Category == cat).OrderBy(f => f.Name)
            .Select(f => new InlineButton((s.Selected.Contains(f.Id) ? "✅ " : "") + f.Name, $"i:t:{f.Id}:{ci}"));
        var rows = Rows(buttons, 2);
        rows.Add(Row(new InlineButton("⬅️ Назад", "i:home"), new InlineButton($"✅ Готово ({s.Selected.Count})", "i:done")));
        return new BotReply($"{CatLabel(cat)}\nНажмите на то, что есть:", rows);
    }

    private async Task<BotReply> IdeaCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct)
    {
        var foods = await FoodsAsync(ct);
        switch (p.Length > 1 ? p[1] : "")
        {
            case "c": return IdeaCategory(s, foods, int.Parse(p[2], CultureInfo.InvariantCulture));
            case "t":
                int id = int.Parse(p[2], CultureInfo.InvariantCulture);
                if (!s.Selected.Add(id)) s.Selected.Remove(id);
                return IdeaCategory(s, foods, int.Parse(p[3], CultureInfo.InvariantCulture));
            case "reset": s.Selected.Clear(); return IdeaHome(s, foods);
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

        var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
        var own = (await _legacy.LoadDishesAsync(member.HouseholdId, foods, ct)).Where(o => PassesFilter(s, o.Model)).ToList();
        var pantry = s.Selected.ToDictionary(id => id, _ => 100000.0);
        var rec = new SmartRecommender();
        var ctx = new RecommendationContext();

        var ownTop = rec.Recommend(own.Select(o => o.Model).ToList(), pantry, ctx, 4)
            .Where(r => r.Coverage.IngredientShare >= 0.5).ToList();

        var ownNames = own.Select(o => o.Model.Name.ToLowerInvariant()).ToHashSet();
        var templates = TemplateDishes.Build(foods).Where(t => !ownNames.Contains(t.Name.ToLowerInvariant()) && PassesFilter(s, t)).ToList();
        var tmplTop = rec.Recommend(templates, pantry, ctx, 3)
            .Where(r => r.Coverage.IngredientShare >= 0.5).ToList();

        s.Results.Clear();
        var sb = new StringBuilder("🍽 Вот что можно приготовить:\n");
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

        if (n == 0)
        {
            var home = IdeaHome(s, foods);
            return home with { Text = "Из этих продуктов ничего подходящего не нашлось 😔 Добавьте ещё продуктов.\n\n" + home.Text };
        }

        sb.AppendLine("\nНажмите номер, чтобы посмотреть состав.");
        var rows = new List<IReadOnlyList<InlineButton>>
        {
            Row(Enumerable.Range(1, n).Select(i => new InlineButton(i.ToString(), $"r:v:{i - 1}")).ToArray()),
            FilterRow(s),
            Row(new InlineButton("🔄 Другие продукты", "r:again"))
        };
        return new BotReply(sb.ToString().TrimEnd(), rows);
    }

    private static bool PassesFilter(Session s, Dish d) =>
        (!s.QuickOnly || d.CookMinutes <= 20) && d.Difficulty <= s.MaxDifficulty;

    private static string DifficultyText(int d) => d switch { 1 => "легко", 3 => "сложно", _ => "средне" };

    private static string TimeInfo(Dish d) => $"⏱ {d.CookMinutes} мин · сложность: {DifficultyText(d.Difficulty)}";

    private static string FilterText(Session s)
    {
        var parts = new List<string>();
        if (s.QuickOnly) parts.Add("до 20 минут");
        if (s.MaxDifficulty == 1) parts.Add("только лёгкие");
        else if (s.MaxDifficulty == 2) parts.Add("не сложные");
        return string.Join(", ", parts);
    }

    private static IReadOnlyList<InlineButton> FilterRow(Session s) => Row(
        new InlineButton((s.QuickOnly ? "✅ " : "") + "⚡ До 20 мин", "i:f:t"),
        new InlineButton(s.MaxDifficulty switch { 1 => "🔧 Только лёгкие", 2 => "🔧 Не сложные", _ => "🔧 Любая сложность" }, "i:f:d"));

    private static readonly int[] MinutePresets = { 10, 15, 20, 30, 45, 60, 90 };

    private static BotReply TimeScreen(string question, string prefix, string back)
    {
        var rows = Rows(MinutePresets.Select(m => new InlineButton($"{m} мин", $"{prefix}:{m}")), 4);
        rows.Add(Row(new InlineButton("⬅️ Назад", back)));
        return new BotReply("⏱ " + question, rows);
    }

    private static BotReply DifficultyScreen(string question, string prefix, string back) => new(
        "🔧 " + question,
        new[]
        {
            Row(new InlineButton("🟢 Легко", $"{prefix}:1"), new InlineButton("🟡 Средне", $"{prefix}:2"), new InlineButton("🔴 Сложно", $"{prefix}:3")),
            Row(new InlineButton("⬅️ Назад", back))
        });

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
            case "again": return IdeaHome(s, foods);
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
                var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
                var d = s.Results[i].Dish;
                await SaveDishAsync(member, d.Name, d.Servings, d.Ingredients.Select(x => (x.Food.Id, x.Grams)).ToList(), ct, d.CookMinutes, d.Difficulty);
                return new BotReply($"✅ «{d.Name}» сохранено в ваши блюда.",
                    new[] { Row(new InlineButton("⬅️ К списку", "r:back")) });
            }
            default: return await IdeaResultsAsync(userId, userName, s, foods, null, ct);
        }
    }

    private async Task<BotReply> DishListAsync(long userId, string userName, CancellationToken ct)
    {
        var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
        var foods = await FoodsAsync(ct);
        var dishes = await _legacy.LoadDishesAsync(member.HouseholdId, foods, ct);

        if (dishes.Count == 0)
            return new BotReply("📖 В вашей базе пока нет блюд.\nНажмите «➕ Добавить блюдо» внизу или посмотрите идеи в «🍳 Что приготовить».");

        var rows = Rows(dishes.Select(d => new InlineButton(
            $"{d.Model.Name} · {d.Model.PerServing.Kcal:F0} ккал", $"d:v:{d.Entity.Id}")), 1);
        return new BotReply($"📖 Ваши блюда ({dishes.Count}):\nНажмите на блюдо, чтобы увидеть состав.", rows);
    }

    private async Task<BotReply> DishCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct)
    {
        string action = p.Length > 1 ? p[1] : "";
        if (action == "list") return await DishListAsync(userId, userName, ct);

        var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
        int id = p.Length > 2 ? int.Parse(p[2], CultureInfo.InvariantCulture) : 0;
        var foods = await FoodsAsync(ct);
        var dishes = await _legacy.LoadDishesAsync(member.HouseholdId, foods, ct);
        var found = dishes.FirstOrDefault(d => d.Entity.Id == id);
        if (found.Model is null) return await DishListAsync(userId, userName, ct);
        var dish = found.Model;

        switch (action)
        {
            case "v":
            {
                var sb = new StringBuilder($"📖 {dish.Name} ({dish.Servings} порц.)\n{TimeInfo(dish)}\n");
                sb.AppendLine("На порцию: " + Nutr(dish.PerServing));
                sb.AppendLine("Всего: " + Nutr(dish.Total));
                sb.AppendLine("\nСостав:");
                foreach (var ing in dish.Ingredients) sb.AppendLine($"• {ing.Food.Name} — {ing.Grams:F0} г");
                return new BotReply(sb.ToString().TrimEnd(), new[]
                {
                    Row(new InlineButton("👩‍🍳 Готовлю", $"d:cook:{id}"), new InlineButton("🍽 Съел(а)", $"d:eat:{id}")),
                    Row(new InlineButton("✏️ Изменить", $"d:edit:{id}"), new InlineButton("🗑 Удалить", $"d:del:{id}")),
                    Row(new InlineButton("⬅️ К списку", "d:list"))
                });
            }
            case "edit":
            case "cook":
                StartEditing(s, found.Entity, dish, action == "cook" ? CookMode.Cook : CookMode.Edit);
                return EditScreen(s, foods, null);
            case "eat":
            {
                var portions = new[] { 0.5, 1, 1.5, 2, 3 };
                return new BotReply($"Сколько порций «{dish.Name}» вы съели?\nНа порцию: {Nutr(dish.PerServing)}",
                    Rows(portions.Select(x => new InlineButton(PortionText(x), $"d:ate:{id}:{x.ToString(CultureInfo.InvariantCulture)}")), 3)
                        .Append(Row(new InlineButton("⬅️ Назад", $"d:v:{id}"))).ToList());
            }
            case "ate":
            {
                double portions = double.Parse(p[3], CultureInfo.InvariantCulture);
                await AddDiaryAsync(member, s, dish.Name + " × " + PortionText(portions), dish.PerServing * portions, ct);
                s.DiaryOffset = 0;
                return await DayAsync(userId, userName, s, ct);
            }
            case "del":
                return new BotReply($"Удалить «{dish.Name}»?", new[]
                {
                    Row(new InlineButton("Да, удалить", $"d:ok:{id}"), new InlineButton("Отмена", $"d:v:{id}"))
                });
            case "ok":
            {
                var entity = await _db.Dishes.FirstOrDefaultAsync(d => d.Id == id && d.HouseholdId == member.HouseholdId, ct);
                if (entity != null)
                {
                    _db.Dishes.Remove(entity);
                    await _db.SaveChangesAsync(ct);
                }
                return await DishListAsync(userId, userName, ct);
            }
            default: return await DishListAsync(userId, userName, ct);
        }
    }

    private async Task<Dish> SaveDishAsync(
        Member member, string name, int servings, IReadOnlyList<(int FoodId, double Grams)> ingredients, CancellationToken ct,
        int minutes = 30, int difficulty = 2)
    {
        var old = await _db.Dishes.Where(d => d.HouseholdId == member.HouseholdId).ToListAsync(ct);
        var same = old.FirstOrDefault(d => d.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        if (same != null) _db.Dishes.Remove(same);

        var grouped = ingredients.GroupBy(i => i.FoodId).Select(g => (FoodId: g.Key, Grams: g.Sum(x => x.Grams))).ToList();
        _db.Dishes.Add(new HouseholdDish
        {
            HouseholdId = member.HouseholdId,
            Name = name,
            Servings = servings,
            CookMinutes = minutes,
            Difficulty = difficulty,
            Ingredients = grouped.Select(g => new HouseholdDishIngredient { FoodId = g.FoodId, Grams = g.Grams }).ToList()
        });
        await _db.SaveChangesAsync(ct);

        var map = (await FoodsAsync(ct)).ToDictionary(f => f.Id);
        return new Dish(name, servings, grouped.Select(g => new DishIngredient(map[g.FoodId], g.Grams)).ToList(), minutes, difficulty);
    }

    private async Task<BotReply> AddHomeAsync(Session s, string? note, CancellationToken ct)
    {
        var foods = await FoodsAsync(ct);
        return AddHome(s, foods, note);
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

    private static BotReply AddCategory(Session s, IReadOnlyList<Food> foods, int ci)
    {
        var cats = Categories(foods);
        if (ci < 0 || ci >= cats.Count) return AddHome(s, foods, null);
        string cat = cats[ci];

        var rows = Rows(foods.Where(f => f.Category == cat).OrderBy(f => f.Name)
            .Select(f => new InlineButton(f.Name, $"a:p:{f.Id}")), 2);
        rows.Add(Row(new InlineButton("⬅️ Назад", "a:home")));
        return new BotReply($"{CatLabel(cat)}\nВыберите продукт:", rows);
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
            case "c": return AddCategory(s, foods, int.Parse(p[2], CultureInfo.InvariantCulture));
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
                s.Step = Step.AddSearch;
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
            {
                if (s.Draft.Count == 0) return AddHome(s, foods, null);
                int difficulty = int.Parse(p[2], CultureInfo.InvariantCulture);
                var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
                var dish = await SaveDishAsync(member, s.DraftName, s.DraftServings, s.Draft.ToList(), ct, s.DraftMinutes, difficulty);
                s.ResetDraft();
                return new BotReply(
                    $"✅ Блюдо «{dish.Name}» сохранено!\n{TimeInfo(dish)}\n\nНа порцию: {Nutr(dish.PerServing)}\nВсего ({dish.Servings} порц.): {Nutr(dish.Total)}",
                    new[]
                    {
                        Row(new InlineButton("➕ Ещё блюдо", "a:again"), new InlineButton("📖 Мои блюда", "d:list"))
                    });
            }
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

    private async Task<BotReply> SearchAsync(Session s, string query, CancellationToken ct)
    {
        s.Step = Step.None;
        var foods = await FoodsAsync(ct);
        string q = FoodMatcher.Normalize(query);

        var hits = foods.Where(f => FoodMatcher.Normalize(f.Name).Contains(q)).OrderBy(f => f.Name).Take(8).ToList();
        if (hits.Count == 0)
        {
            var one = FoodMatcher.Find(query, foods);
            if (one != null) hits.Add(one);
        }

        if (hits.Count == 0)
        {
            s.NewFoodName = query.Trim();
            return new BotReply($"Не нашла «{query.Trim()}». Можно добавить его самому.", new[]
            {
                Row(new InlineButton($"➕ Создать «{s.NewFoodName}»", "a:new")),
                Row(new InlineButton("⬅️ Назад", "a:home"))
            });
        }

        var rows = Rows(hits.Select(f => new InlineButton(f.Name, $"a:p:{f.Id}")), 2);
        rows.Add(Row(new InlineButton("⬅️ Назад", "a:home")));
        return new BotReply("Нашла. Выберите продукт:", rows);
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
            Category = "Свои продукты",
            KcalPer100 = nums[0], ProteinPer100 = nums[1], FatPer100 = nums[2], CarbsPer100 = nums[3],
            PricePer100 = nums.Count == 5 ? nums[4] : 0.3,
            MaxGrams = 400
        };
        _db.Foods.Add(food);
        await _db.SaveChangesAsync(ct);

        s.Step = Step.None;
        s.PendingFoodId = food.Id;
        return GramsScreen(food);
    }

    private async Task<BotReply> PairAsync(long userId, string userName, CancellationToken ct)
    {
        var member = await _legacy.EnsureMemberAsync(userId, userName, ct);
        int count = await _db.Members.CountAsync(m => m.HouseholdId == member.HouseholdId, ct);
        return new BotReply(
            "👫 Общая база блюд для двоих\n\n" +
            $"Ваш код: {member.Household!.Code}\n" +
            $"Участников: {count}\n\n" +
            "Партнёр открывает этого бота, нажимает «Пара» → «Ввести код партнёра» и вводит ваш код. " +
            "После этого у вас будут общие блюда.",
            new[] { Row(new InlineButton("🔗 Ввести код партнёра", "p:join")) });
    }
}
