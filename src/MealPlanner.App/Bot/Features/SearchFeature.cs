using MealPlanner.App.Services;

namespace MealPlanner.App.Bot;

/// <summary>
/// Text product search: the result is shown in the section where the search was started
/// </summary>
internal sealed class SearchFeature : FeatureBase
{
    public SearchFeature(BotContext ctx) : base(ctx)
    {
    }

    public override IReadOnlyCollection<Step> Steps { get; } = new[] { Step.SearchFood };

    public override async Task<BotReply> HandleStepAsync(long userId, string userName, Session s, string text, CancellationToken ct)
    {
        s.Step = Step.None;
        s.SearchQuery = text;
        return UiKit.SearchReply(s, await FoodsAsync(ct), 0);
    }
}
