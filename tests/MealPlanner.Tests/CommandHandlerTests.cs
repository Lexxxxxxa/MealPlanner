using System.Text.RegularExpressions;
using MealPlanner.App.Bot;
using MealPlanner.App.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MealPlanner.Tests;

public sealed class BotFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    public AppDbContext Db { get; }
    public CommandHandler Bot { get; }

    public BotFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        Db = new AppDbContext(options);
        AppDbContext.Initialize(Db);
        Bot = new CommandHandler(Db);
    }

    public Task<string> Say(long user, string text) => Bot.HandleAsync(user, $"user{user}", text);

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

public class CommandHandlerTests
{
    private const long Alice = 1, Bob = 2;

    [Fact]
    public async Task Start_ReturnsHouseholdCode()
    {
        using var f = new BotFixture();
        var reply = await f.Say(Alice, "/start");

        Assert.Matches(new Regex(@"Код вашего домохозяйства: [A-Z0-9]{6}"), reply);
    }

    [Fact]
    public async Task Partner_SeesSharedDishesAfterJoin()
    {
        using var f = new BotFixture();
        var start = await f.Say(Alice, "/start");
        string code = Regex.Match(start, @"домохозяйства: ([A-Z0-9]{6})").Groups[1].Value;

        await f.Say(Alice, "/dish борщ: свёкла 200, картофель 300, говядина 150; 4 порции");
        var join = await f.Say(Bob, "/join " + code);
        var list = await f.Say(Bob, "/dishes");

        Assert.Contains("Участников: 2", join);
        Assert.Contains("борщ", list);
    }

    [Fact]
    public async Task Dish_ReportsCaloriesPerServing()
    {
        using var f = new BotFixture();
        var reply = await f.Say(Alice, "/dish гречка с курицей: гречка 100, куриная грудка 200; 2 порции");

        Assert.Contains("сохранено", reply);
        Assert.Contains("На порцию", reply);
        Assert.Contains("ккал", reply);
    }

    [Fact]
    public async Task Dish_WithUnknownFood_AsksToAddIt()
    {
        using var f = new BotFixture();
        var reply = await f.Say(Alice, "/dish суп: драконий фрукт 100");

        Assert.Contains("драконий фрукт", reply);
        Assert.Contains("/food", reply);
    }

    [Fact]
    public async Task Food_CanBeAddedAndThenUsed()
    {
        using var f = new BotFixture();
        await f.Say(Alice, "/food Сыр фета 264 14 21 4 1.2");
        var reply = await f.Say(Alice, "/calc салат: сыр фета 100, огурцы 200");

        Assert.Contains("ккал", reply);
        Assert.DoesNotContain("Не знаю", reply);
    }

    [Fact]
    public async Task Cook_PutsCookableDishFirst()
    {
        using var f = new BotFixture();
        await f.Say(Alice, "/dish омлет: яйца 3 шт, молоко 100");
        await f.Say(Alice, "/dish борщ: свёкла 200, говядина 150, капуста 200");
        await f.Say(Alice, "/have яйца 300, молоко 500");

        var reply = await f.Say(Alice, "/cook");

        int omelette = reply.IndexOf("омлет", StringComparison.Ordinal);
        int borscht = reply.IndexOf("борщ", StringComparison.Ordinal);
        Assert.True(omelette >= 0 && omelette < borscht, reply);
        Assert.Contains("можно готовить сейчас", reply);
    }

    [Fact]
    public async Task Cooked_DeductsIngredientsFromPantry()
    {
        using var f = new BotFixture();
        await f.Say(Alice, "/dish омлет: яйца 3 шт, молоко 100");
        await f.Say(Alice, "/have яйца 300, молоко 500");

        await f.Say(Alice, "/cooked 1");
        var pantry = await f.Say(Alice, "/pantry");

        Assert.Contains("Молоко 1.5% — 400 г", pantry);
    }

    [Fact]
    public async Task Idea_WithEmptyPantry_SuggestsTemplates()
    {
        using var f = new BotFixture();
        var reply = await f.Say(Alice, "/idea");

        Assert.Contains("состав:", reply);
    }

    [Fact]
    public async Task Menu_FromBroadPantry_IsBuiltFromStock()
    {
        using var f = new BotFixture();
        await f.Say(Alice, "/have овсяные хлопья 150, макароны 150, рис 150, чечевица 120, творог 400, яйца 250, " +
                           "куриная грудка 300, картофель 500, морковь 300, масло подсолнечное 40, масло сливочное 30, " +
                           "хлеб 250, молоко 500, банан 300, сыр 100");

        var reply = await f.Say(Alice, "/menu 2000");

        Assert.Contains("из ваших запасов", reply);
    }

    [Fact]
    public async Task Menu_FromTinyPantry_ExplainsWhatToBuy()
    {
        using var f = new BotFixture();
        await f.Say(Alice, "/have рис 300");

        var reply = await f.Say(Alice, "/menu 2000");

        Assert.Contains("докупите", reply);
    }

    [Fact]
    public async Task UnknownCommand_ShowsHelp()
    {
        using var f = new BotFixture();
        var reply = await f.Say(Alice, "/абракадабра");

        Assert.Contains("Неизвестная команда", reply);
    }
}
