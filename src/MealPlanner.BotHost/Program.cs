using MealPlanner.App.Bot;
using MealPlanner.App.Data;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

string? token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("Не задано TELEGRAM_BOT_TOKEN. Створіть бота через @BotFather і задайте токен у змінній середовища.");
    return 1;
}

string dbPath = Environment.GetEnvironmentVariable("MEALPLANNER_DB") ?? "mealplanner.db";
var allowed = (Environment.GetEnvironmentVariable("ALLOWED_USER_IDS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(s => long.TryParse(s, out var id) ? id : (long?)null)
    .Where(id => id.HasValue)
    .Select(id => id!.Value)
    .ToHashSet();

var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={dbPath}").Options;
using (var db = new AppDbContext(options))
{
    AppDbContext.Initialize(db);
}

var bot = new TelegramBotClient(token);
var me = await bot.GetMeAsync();
Console.WriteLine($"Бот запущено: @{me.Username}. Ctrl+C — зупинити.");
if (allowed.Count == 0)
    Console.WriteLine("УВАГА: ALLOWED_USER_IDS не задано — ботом може користуватися будь-хто, хто знає його ім'я.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

bot.StartReceiving(
    updateHandler: async (client, update, ct) =>
    {
        var message = update.Message;
        if (update.Type != UpdateType.Message || message?.Text is null || message.From is null) return;

        long userId = message.From.Id;
        if (allowed.Count > 0 && !allowed.Contains(userId))
        {
            await client.SendTextMessageAsync(message.Chat.Id,
                $"Доступ закрыт. Ваш Telegram-ID: {userId}", cancellationToken: ct);
            return;
        }

        string reply;
        try
        {
            using var db = new AppDbContext(options);
            var handler = new CommandHandler(db);
            string name = message.From.FirstName ?? message.From.Username ?? "друг";
            reply = await handler.HandleAsync(userId, name, message.Text, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            reply = "Что-то пошло не так. Попробуйте ещё раз.";
        }

        if (reply.Length > 4000) reply = reply[..4000] + "…";
        await client.SendTextMessageAsync(message.Chat.Id, reply, cancellationToken: ct);
    },
    pollingErrorHandler: (client, ex, ct) =>
    {
        Console.Error.WriteLine($"Ошибка опроса Telegram: {ex.Message}");
        return Task.CompletedTask;
    },
    receiverOptions: new ReceiverOptions { AllowedUpdates = new[] { UpdateType.Message } },
    cancellationToken: cts.Token);

try { await Task.Delay(Timeout.Infinite, cts.Token); }
catch (OperationCanceledException) { }

Console.WriteLine("Бот зупинено.");
return 0;
