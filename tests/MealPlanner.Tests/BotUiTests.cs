using MealPlanner.App.Bot;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MealPlanner.Tests;

public class BotUiTests
{
    private const long Alice = 11, Bob = 22;

    private sealed class Ui : IDisposable
    {
        private readonly BotFixture _fx = new();
        private readonly SessionStore _sessions = new();

        public Task<BotReply> Msg(long user, string text) =>
            new BotUi(_fx.Db, _sessions).HandleMessageAsync(user, $"user{user}", text);

        public Task<BotReply> Tap(long user, string data) =>
            new BotUi(_fx.Db, _sessions).HandleCallbackAsync(user, $"user{user}", data);

        public async Task<int> Id(string foodName) =>
            (await _fx.Db.Foods.FirstAsync(f => f.Name == foodName)).Id;

        public void Dispose() => _fx.Dispose();
    }

    private static bool HasButton(BotReply r, string data) =>
        r.Buttons != null && r.Buttons.SelectMany(row => row).Any(b => b.Data == data);

    private static bool HasButtonText(BotReply r, string text) =>
        r.Buttons != null && r.Buttons.SelectMany(row => row).Any(b => b.Text.Contains(text));

    private static async Task SaveDish(Ui ui, string name, params (string food, int grams)[] items)
    {
        await ui.Msg(Alice, "➕ Добавить блюдо");
        await ui.Msg(Alice, name);
        foreach (var (food, grams) in items)
        {
            int id = await ui.Id(food);
            await ui.Tap(Alice, $"a:p:{id}");
            await ui.Tap(Alice, $"a:g:{id}:{grams}");
        }
        await ui.Tap(Alice, "a:done");
        await ui.Tap(Alice, "a:s:1");
    }

    [Fact]
    public async Task IdeaButton_ShowsCategoriesAndDone()
    {
        using var ui = new Ui();
        var r = await ui.Msg(Alice, "🍳 Что приготовить");

        Assert.True(HasButton(r, "i:c:0"));
        Assert.True(HasButton(r, "i:done"));
        Assert.True(HasButtonText(r, "Мясо и рыба"));
    }

    [Fact]
    public async Task AddDishWizard_SavesDishWithCalories()
    {
        using var ui = new Ui();
        await ui.Msg(Alice, "➕ Добавить блюдо");
        var home = await ui.Msg(Alice, "Омлет");
        Assert.Contains("Омлет", home.Text);
        Assert.True(HasButton(home, "a:done"));

        int egg = await ui.Id("Яйца");
        var grams = await ui.Tap(Alice, $"a:p:{egg}");
        Assert.True(HasButton(grams, $"a:g:{egg}:150"));

        await ui.Tap(Alice, $"a:g:{egg}:150");
        var servings = await ui.Tap(Alice, "a:done");
        Assert.True(HasButton(servings, "a:s:2"));

        var saved = await ui.Tap(Alice, "a:s:1");
        Assert.Contains("сохранено", saved.Text);
        Assert.Contains("ккал", saved.Text);

        var list = await ui.Msg(Alice, "📖 Мои блюда");
        Assert.True(HasButtonText(list, "Омлет"));
    }

    [Fact]
    public async Task AddDish_WithoutProducts_AsksToAddOne()
    {
        using var ui = new Ui();
        await ui.Msg(Alice, "➕ Добавить блюдо");
        await ui.Msg(Alice, "Пустое");
        var r = await ui.Tap(Alice, "a:done");

        Assert.Contains("хотя бы один продукт", r.Text);
    }

    [Fact]
    public async Task CustomGrams_AcceptsPiecesAndKilograms()
    {
        using var ui = new Ui();
        await ui.Msg(Alice, "➕ Добавить блюдо");
        await ui.Msg(Alice, "Яичница");
        int egg = await ui.Id("Яйца");
        await ui.Tap(Alice, $"a:gx:{egg}");
        var r = await ui.Msg(Alice, "3 шт");

        Assert.Contains("Яйца 165 г", r.Text);
    }

    [Fact]
    public async Task Idea_RanksOwnCookableDishAndMarksItReady()
    {
        using var ui = new Ui();
        await SaveDish(ui, "Омлет", ("Яйца", 165), ("Молоко 1.5%", 100));

        await ui.Msg(Alice, "🍳 Что приготовить");
        await ui.Tap(Alice, $"i:t:{await ui.Id("Яйца")}:0");
        await ui.Tap(Alice, $"i:t:{await ui.Id("Молоко 1.5%")}:0");
        var r = await ui.Tap(Alice, "i:done");

        Assert.Contains("Омлет", r.Text);
        Assert.Contains("всё есть", r.Text);
        Assert.True(HasButton(r, "r:v:0"));
    }

    [Fact]
    public async Task Idea_SuggestsTemplateAndCanSaveIt()
    {
        using var ui = new Ui();
        await ui.Msg(Alice, "🍳 Что приготовить");
        foreach (var f in new[] { "Гречка", "Куриная грудка", "Лук репчатый", "Масло подсолнечное" })
            await ui.Tap(Alice, $"i:t:{await ui.Id(f)}:0");

        var results = await ui.Tap(Alice, "i:done");
        Assert.Contains("Гречка с курицей", results.Text);

        var detail = await ui.Tap(Alice, "r:v:0");
        Assert.True(HasButton(detail, "r:save:0"));

        var saved = await ui.Tap(Alice, "r:save:0");
        Assert.Contains("сохранено", saved.Text);

        var list = await ui.Msg(Alice, "📖 Мои блюда");
        Assert.True(HasButtonText(list, "Гречка с курицей"));
    }

    [Fact]
    public async Task Idea_FromTypedList_FindsTemplate()
    {
        using var ui = new Ui();
        await ui.Msg(Alice, "🍳 Что приготовить");
        await ui.Tap(Alice, "i:txt");
        var r = await ui.Msg(Alice, "яйца, молоко, помидоры, масло сливочное");

        Assert.Contains("Омлет с помидорами", r.Text);
    }

    [Fact]
    public async Task Idea_WithNothingSelected_StaysOnSelection()
    {
        using var ui = new Ui();
        await ui.Msg(Alice, "🍳 Что приготовить");
        var r = await ui.Tap(Alice, "i:done");

        Assert.Contains("ничего не выбрано", r.Text);
        Assert.True(HasButton(r, "i:done"));
    }

    [Fact]
    public async Task DishCanBeDeletedAfterConfirmation()
    {
        using var ui = new Ui();
        await SaveDish(ui, "Омлет", ("Яйца", 165));

        var list = await ui.Msg(Alice, "📖 Мои блюда");
        string viewData = list.Buttons!.SelectMany(r => r).First(b => b.Data.StartsWith("d:v:")).Data;
        string id = viewData.Split(':')[2];

        var confirm = await ui.Tap(Alice, $"d:del:{id}");
        Assert.True(HasButton(confirm, $"d:ok:{id}"));

        var after = await ui.Tap(Alice, $"d:ok:{id}");
        Assert.Contains("пока нет блюд", after.Text);
    }

    [Fact]
    public async Task Pair_ShowsCodeAndPartnerCanJoin()
    {
        using var ui = new Ui();
        await SaveDish(ui, "Омлет", ("Яйца", 165));

        var pair = await ui.Msg(Alice, "👫 Пара");
        Assert.True(HasButton(pair, "p:join"));
        string code = System.Text.RegularExpressions.Regex.Match(pair.Text, @"Ваш код: ([A-Z0-9]{6})").Groups[1].Value;
        Assert.NotEqual(string.Empty, code);

        await ui.Tap(Bob, "p:join");
        var joined = await ui.Msg(Bob, code);
        Assert.Contains("Участников: 2", joined.Text);

        var list = await ui.Msg(Bob, "📖 Мои блюда");
        Assert.True(HasButtonText(list, "Омлет"));
    }

    [Fact]
    public async Task UnknownProduct_CanBeCreatedAndUsed()
    {
        using var ui = new Ui();
        await ui.Msg(Alice, "➕ Добавить блюдо");
        await ui.Msg(Alice, "Салат");
        await ui.Tap(Alice, "a:find");
        var notFound = await ui.Msg(Alice, "драконий фрукт");
        Assert.True(HasButton(notFound, "a:new"));

        var ask = await ui.Tap(Alice, "a:new");
        Assert.Contains("калории", ask.Text);

        var grams = await ui.Msg(Alice, "60 1 0.2 15");
        Assert.Contains("драконий фрукт", grams.Text);
    }

    [Fact]
    public async Task Search_FindsProductByPartOfName()
    {
        using var ui = new Ui();
        await ui.Msg(Alice, "➕ Добавить блюдо");
        await ui.Msg(Alice, "Бутерброд");
        await ui.Tap(Alice, "a:find");
        var r = await ui.Msg(Alice, "сыр");

        Assert.True(HasButtonText(r, "Сыр гауда"));
    }

    [Fact]
    public async Task SlashStart_ShowsGreetingWithoutButtons()
    {
        using var ui = new Ui();
        var r = await ui.Msg(Alice, "/start");

        Assert.Contains("Привет", r.Text);
        Assert.Null(r.Buttons);
    }
}
