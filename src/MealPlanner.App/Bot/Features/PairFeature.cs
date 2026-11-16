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
/// Shared household for two: code and partner joining
/// </summary>
internal sealed class PairFeature : FeatureBase
{
    public PairFeature(BotContext ctx) : base(ctx)
    {
    }

    public override IReadOnlyCollection<string> Prefixes { get; } = new[] { "p" };

    public override IReadOnlyCollection<Step> Steps { get; } = new[] { Step.JoinCode };

    public override Task<BotReply> HandleCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct)
    {
        s.Step = Step.JoinCode;
        return Task.FromResult(new BotReply("Введите код партнёра (6 символов, например ABC123):"));
    }

    public override async Task<BotReply> HandleStepAsync(long userId, string userName, Session s, string text, CancellationToken ct)
    {
        s.Step = Step.None;
        return new BotReply(await Ctx.Commands.JoinAsync(userId, userName, text, ct));
    }

    internal async Task<BotReply> PairAsync(long userId, string userName, CancellationToken ct)
    {
        var member = await MemberAsync(userId, userName, ct);
        int count = await Services.Households.CountMembersAsync(member.HouseholdId, ct);
        return new BotReply(
            "👫 Общая база блюд для двоих\n\n" +
            $"Ваш код: {member.Household!.Code}\n" +
            $"Участников: {count}\n\n" +
            "Партнёр открывает этого бота, нажимает «Пара» → «Ввести код партнёра» и вводит ваш код. " +
            "После этого у вас будут общие блюда.",
            new[] { Row(new InlineButton("🔗 Ввести код партнёра", "p:join")) });
    }
}
