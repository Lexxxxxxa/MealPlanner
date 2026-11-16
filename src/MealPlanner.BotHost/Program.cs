using MealPlanner.App.Bot;
using MealPlanner.App.Data;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

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
using (var initDb = new AppDbContext(options))
{
    DatabaseInitializer.Run(initDb);
}

var sessions = new SessionStore();
var bot = new TelegramBotClient(token);
var me = await bot.GetMe();
Console.WriteLine($"Бот запущено: @{me.Username}. Ctrl+C — зупинити.");
if (allowed.Count == 0)
    Console.WriteLine("УВАГА: ALLOWED_USER_IDS не задано — ботом може користуватися будь-хто, хто знає його ім'я.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

bot.StartReceiving(
    async (client, update, ct) =>
    {
        try
        {
            if (update.CallbackQuery is { Data: not null, Message: not null } cq)
            {
                await HandleCallback(client, cq, ct);
            }
            else if (update.Message is { Text: not null, From: not null } message)
            {
                await HandleMessage(client, message, ct);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
    },
    (client, ex, ct) =>
    {
        Console.Error.WriteLine($"Ошибка опроса Telegram: {ex.Message}");
        return Task.CompletedTask;
    },
    new Telegram.Bot.Polling.ReceiverOptions { AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery } },
    cts.Token);

try { await Task.Delay(Timeout.Infinite, cts.Token); }
catch (OperationCanceledException) { }

Console.WriteLine("Бот зупинено.");
return 0;

bool IsAllowed(long userId) => allowed.Count == 0 || allowed.Contains(userId);

static string NameOf(User u) => u.FirstName ?? u.Username ?? "друг";

async Task<BotReply> Run(long userId, string name, Func<BotUi, Task<BotReply>> action)
{
    try
    {
        using var db = new AppDbContext(options);
        return await action(new BotUi(db, sessions));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        return new BotReply("Что-то пошло не так. Попробуйте ещё раз.");
    }
}

static InlineKeyboardMarkup ToInline(BotReply reply) =>
    new(reply.Buttons!.Select(row => row.Select(b => InlineKeyboardButton.WithCallbackData(b.Text, b.Data))));

static ReplyKeyboardMarkup MainMenu() =>
    new(BotUi.MainMenu.Select(row => row.Select(t => new KeyboardButton(t)))) { ResizeKeyboard = true };

static string Clip(string text) => text.Length > 4000 ? text[..4000] + "…" : text;

async Task HandleMessage(ITelegramBotClient client, Message message, CancellationToken ct)
{
    long userId = message.From!.Id;
    if (!IsAllowed(userId))
    {
        await client.SendMessage(message.Chat.Id, $"Доступ закрыт. Ваш Telegram-ID: {userId}", cancellationToken: ct);
        return;
    }

    var reply = await Run(userId, NameOf(message.From), ui => ui.HandleMessageAsync(userId, NameOf(message.From), message.Text!, ct));
    if (reply.Buttons is { Count: > 0 })
        await client.SendMessage(message.Chat.Id, Clip(reply.Text), replyMarkup: ToInline(reply), cancellationToken: ct);
    else
        await client.SendMessage(message.Chat.Id, Clip(reply.Text), replyMarkup: MainMenu(), cancellationToken: ct);
}

async Task HandleCallback(ITelegramBotClient client, CallbackQuery cq, CancellationToken ct)
{
    long userId = cq.From.Id;
    await client.AnswerCallbackQuery(cq.Id, cancellationToken: ct);
    if (!IsAllowed(userId)) return;

    var reply = await Run(userId, NameOf(cq.From), ui => ui.HandleCallbackAsync(userId, NameOf(cq.From), cq.Data!, ct));
    long chatId = cq.Message!.Chat.Id;
    try
    {
        if (reply.Buttons is { Count: > 0 })
            await client.EditMessageText(chatId, cq.Message.MessageId, Clip(reply.Text), replyMarkup: ToInline(reply), cancellationToken: ct);
        else
            await client.EditMessageText(chatId, cq.Message.MessageId, Clip(reply.Text), cancellationToken: ct);
    }
    catch (Telegram.Bot.Exceptions.ApiRequestException)
    {
    }
}
