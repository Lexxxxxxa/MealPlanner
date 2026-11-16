using MealPlanner.App.Data;
using MealPlanner.App.Services;

namespace MealPlanner.App.Bot;

/// <summary>
/// Button-driven bot interface, independent of Telegram: takes text or pressed button data and returns a <see cref="BotReply"/>
/// Only routes: slash commands go to <see cref="CommandHandler"/>, buttons and dialog steps go to the matching feature
/// </summary>
public class BotUi
{
    public const string KeyIdea = "Что приготовить";
    public const string KeyDishes = "Мои блюда";
    public const string KeyAdd = "Добавить блюдо";
    public const string KeyPair = "Пара";
    public const string KeyHelp = "Помощь";
    public const string KeyDiary = "Дневник";
    public const string KeyCatalog = "База блюд";

    public static readonly string[][] MainMenu =
    {
        new[] { "🍳 " + KeyIdea, "📖 " + KeyDishes },
        new[] { "📚 " + KeyCatalog, "➕ " + KeyAdd },
        new[] { "📅 " + KeyDiary, "👫 " + KeyPair },
        new[] { "❓ " + KeyHelp }
    };

    private readonly BotContext _ctx;
    private readonly Dictionary<string, IBotFeature> _byPrefix = new();
    private readonly Dictionary<Step, IBotFeature> _byStep = new();

    public BotUi(AppDbContext db, SessionStore sessions, Func<DateOnly>? today = null)
        : this(AppServices.Create(db), sessions, today)
    {
    }

    public BotUi(AppServices services, SessionStore sessions, Func<DateOnly>? today = null)
    {
        _ctx = new BotContext(services, sessions, today ?? (() => DateOnly.FromDateTime(DateTime.Now)));
        _ctx.Commands = new CommandHandler(services);
        _ctx.Features = new BotFeatures(_ctx);

        foreach (var feature in _ctx.Features.All)
        {
            foreach (var prefix in feature.Prefixes) _byPrefix[prefix] = feature;
            foreach (var step in feature.Steps) _byStep[step] = feature;
        }
    }

    private BotFeatures F => _ctx.Features;

    public async Task<BotReply> HandleMessageAsync(long userId, string userName, string text, CancellationToken ct = default)
    {
        string t = (text ?? string.Empty).Trim();
        var session = _ctx.Sessions.Get(userId);

        if (t.StartsWith('/')) return await HandleSlashAsync(userId, userName, session, t, ct);

        var entry = await OpenSectionAsync(userId, userName, session, t, ct);
        if (entry != null) return entry;

        if (_byStep.TryGetValue(session.Step, out var feature))
            return await feature.HandleStepAsync(userId, userName, session, t, ct);

        if (t.Contains(':'))
            return new BotReply(await _ctx.Commands.HandleAsync(userId, userName, t, ct));

        return new BotReply("Выберите действие кнопкой внизу 👇");
    }

    public async Task<BotReply> HandleCallbackAsync(long userId, string userName, string data, CancellationToken ct = default)
    {
        var session = _ctx.Sessions.Get(userId);
        var parts = data.Split(':');

        return _byPrefix.TryGetValue(parts[0], out var feature)
            ? await feature.HandleCallbackAsync(userId, userName, session, parts, ct)
            : new BotReply("Не понял кнопку. Выберите действие в меню внизу.");
    }

    private async Task<BotReply> HandleSlashAsync(long userId, string userName, Session session, string text, CancellationToken ct)
    {
        string cmd = text.Split(' ', '\n')[0].ToLowerInvariant();
        int at = cmd.IndexOf('@');
        if (at > 0) cmd = cmd[..at];

        if (cmd is "/start" or "/menu")
        {
            session.Step = Step.None;
            var member = await _ctx.Services.Households.EnsureMemberAsync(userId, userName, ct);
            return new BotReply($"Привет, {member.Name}! 👋\nЯ помогу решить, что приготовить, и посчитаю калории.\nВыберите действие кнопкой внизу.");
        }
        if (cmd == "/help") return Help();

        return new BotReply(await _ctx.Commands.HandleAsync(userId, userName, text, ct));
    }

    private async Task<BotReply?> OpenSectionAsync(long userId, string userName, Session session, string text, CancellationToken ct)
    {
        if (IsKey(text, KeyIdea)) return await F.Idea.IdeaHomeAsync(userId, userName, reset: true, ct);
        if (IsKey(text, KeyDishes)) return await F.Dishes.OpenAsync(userId, userName, session, ct);
        if (IsKey(text, KeyCatalog)) return await F.Catalog.OpenAsync(session, ct);
        if (IsKey(text, KeyAdd)) return F.AddDish.Start(session);
        if (IsKey(text, KeyPair)) { session.Step = Step.None; return await F.Pair.PairAsync(userId, userName, ct); }
        if (IsKey(text, KeyDiary)) return await F.Diary.OpenAsync(userId, userName, session, ct);
        if (IsKey(text, KeyHelp)) { session.Step = Step.None; return Help(); }
        return null;
    }

    private static bool IsKey(string text, string key) =>
        text.Length <= key.Length + 4 && text.EndsWith(key, StringComparison.OrdinalIgnoreCase);

    private static BotReply Help() => new(
        "Как пользоваться:\n\n" +
        "🍳 Что приготовить — отметьте продукты, которые есть сейчас, и я подскажу блюда: из того, что есть, или с докупкой недостающего.\n" +
        "📖 Мои блюда — ваши сохранённые блюда с калориями, рецептом, поиск по названию и продукту.\n" +
        "📚 База блюд — около 400 готовых блюд по категориям с пошаговым рецептом: смотрите состав и приготовление, сохраняйте в свои, записывайте в дневник.\n" +
        "➕ Добавить блюдо — выберите продукты кнопками и граммы, укажите время и опишите, как готовить. Калории посчитаются сами.\n" +
        "✏️ В блюде можно изменить продукты, граммы и рецепт, а «Готовлю» пересчитает калории по тому, что вы реально положили.\n" +
        "📅 Дневник — что вы съели за день: калории и БЖВ. Норма считается по вашему ИМТ, активности и цели (похудеть, держать вес или набрать).\n" +
        "👫 Пара — общая база блюд для двоих.");
}
