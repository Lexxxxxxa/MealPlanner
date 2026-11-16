using MealPlanner.App.Data;
using MealPlanner.App.Services;
using MealPlanner.Core.Models;

namespace MealPlanner.App.Bot;

/// <summary>
/// Shared dependencies of bot features: services, session store, current date and references to other features
/// </summary>
internal sealed class BotContext
{
    public BotContext(AppServices services, SessionStore sessions, Func<DateOnly> today)
    {
        Services = services;
        Sessions = sessions;
        Today = today;
    }

    public AppServices Services { get; }

    public SessionStore Sessions { get; }

    public Func<DateOnly> Today { get; }

    public CommandHandler Commands { get; set; } = null!;

    public BotFeatures Features { get; set; } = null!;
}

/// <summary>
/// Set of all features, needed so that one feature can open a screen of another
/// </summary>
internal sealed class BotFeatures
{
    public BotFeatures(BotContext ctx)
    {
        Idea = new IdeaFeature(ctx);
        AddDish = new AddDishFeature(ctx);
        Dishes = new DishesFeature(ctx);
        Edit = new EditFeature(ctx);
        Catalog = new CatalogFeature(ctx);
        Diary = new DiaryFeature(ctx);
        Fit = new FitFeature(ctx);
        Profile = new ProfileFeature(ctx);
        Pair = new PairFeature(ctx);
        Search = new SearchFeature(ctx);
    }

    public IdeaFeature Idea { get; }
    public AddDishFeature AddDish { get; }
    public DishesFeature Dishes { get; }
    public EditFeature Edit { get; }
    public CatalogFeature Catalog { get; }
    public DiaryFeature Diary { get; }
    public FitFeature Fit { get; }
    public ProfileFeature Profile { get; }
    public PairFeature Pair { get; }
    public SearchFeature Search { get; }

    public IEnumerable<IBotFeature> All => new IBotFeature[] { Idea, AddDish, Dishes, Edit, Catalog, Diary, Profile, Pair, Search };
}

/// <summary>
/// Bot feature that handles buttons with given callback prefixes and text steps of the dialog
/// A new feature is added by implementing this interface without changes in the router
/// </summary>
internal interface IBotFeature
{
    IReadOnlyCollection<string> Prefixes { get; }

    IReadOnlyCollection<Step> Steps { get; }

    Task<BotReply> HandleCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct);

    Task<BotReply> HandleStepAsync(long userId, string userName, Session s, string text, CancellationToken ct);
}

internal abstract class FeatureBase : IBotFeature
{
    private static readonly string[] NoPrefixes = Array.Empty<string>();
    private static readonly Step[] NoSteps = Array.Empty<Step>();

    protected FeatureBase(BotContext ctx) => Ctx = ctx;

    protected BotContext Ctx { get; }

    protected AppServices Services => Ctx.Services;

    public virtual IReadOnlyCollection<string> Prefixes => NoPrefixes;

    public virtual IReadOnlyCollection<Step> Steps => NoSteps;

    public virtual Task<BotReply> HandleCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct) =>
        Task.FromResult(new BotReply("Не понял кнопку. Выберите действие в меню внизу."));

    public virtual Task<BotReply> HandleStepAsync(long userId, string userName, Session s, string text, CancellationToken ct) =>
        Task.FromResult(new BotReply("Выберите действие кнопкой внизу 👇"));

    protected Task<Member> MemberAsync(long userId, string userName, CancellationToken ct) =>
        Services.Households.EnsureMemberAsync(userId, userName, ct);

    protected Task<List<Food>> FoodsAsync(CancellationToken ct) => Services.Foods.ListAsync(ct);
}
