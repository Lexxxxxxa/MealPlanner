namespace MealPlanner.App.Bot;

public record InlineButton(string Text, string Data);

/// <summary>
/// Bot reply: text and optional buttons under the message
/// Without buttons the host shows the persistent main menu at the bottom of the screen
/// </summary>
public record BotReply(string Text, IReadOnlyList<IReadOnlyList<InlineButton>>? Buttons = null);
