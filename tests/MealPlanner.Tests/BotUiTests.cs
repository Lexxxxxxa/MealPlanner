using MealPlanner.App.Bot;
using MealPlanner.App.Data;
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
        public DateOnly Today { get; set; } = new(2026, 11, 23);
        public AppDbContext Db => _fx.Db;

        public Task<BotReply> Msg(long user, string text) =>
            new BotUi(_fx.Db, _sessions, () => Today).HandleMessageAsync(user, $"user{user}", text);

        public Task<BotReply> Tap(long user, string data) =>
            new BotUi(_fx.Db, _sessions, () => Today).HandleCallbackAsync(user, $"user{user}", data);

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

    [Fact]
    public async Task EditDish_ChangesGramsAndRecalculatesKcal()
    {
        using var ui = new Ui();
        await SaveDish(ui, "Каша", ("Рис", 100));
        int dishId = (await ui.Db.Dishes.FirstAsync()).Id;

        await ui.Tap(Alice, $"d:edit:{dishId}");
        var r = await ui.Tap(Alice, "e:a:0:50");
        Assert.Contains("150 г", r.Text);

        var saved = await ui.Tap(Alice, "e:save");
        Assert.Contains("обновлён", saved.Text);
        Assert.Contains("540 ккал", saved.Text);

        var ing = await ui.Db.DishIngredients.AsNoTracking().SingleAsync();
        Assert.Equal(150, ing.Grams, 3);
    }

    [Fact]
    public async Task EditDish_CanAddProductAndChangeServings()
    {
        using var ui = new Ui();
        await SaveDish(ui, "Каша", ("Рис", 100));
        int dishId = (await ui.Db.Dishes.FirstAsync()).Id;
        int milk = await ui.Id("Молоко 1.5%");

        await ui.Tap(Alice, $"d:edit:{dishId}");
        await ui.Tap(Alice, $"e:g:{milk}:200");
        await ui.Tap(Alice, "e:s:2");
        await ui.Tap(Alice, "e:save");

        var dish = await ui.Db.Dishes.Include(d => d.Ingredients).AsNoTracking().SingleAsync();
        Assert.Equal(2, dish.Servings);
        Assert.Equal(2, dish.Ingredients.Count);
    }

    [Fact]
    public async Task CookMode_ShowsRecipeVersusActual_AndLogsActualPortion()
    {
        using var ui = new Ui();
        await SaveDish(ui, "Каша", ("Рис", 100));
        int dishId = (await ui.Db.Dishes.FirstAsync()).Id;

        await ui.Tap(Alice, $"d:cook:{dishId}");
        var r = await ui.Tap(Alice, "e:a:0:50");
        await ui.Tap(Alice, "e:a:0:50");
        r = await ui.Tap(Alice, "e:home");
        Assert.Contains("по рецепту 100", r.Text);
        Assert.Contains("+360 ккал на порцию", r.Text);

        await ui.Tap(Alice, "e:eat");
        var day = await ui.Tap(Alice, "e:ate:1");
        Assert.Contains("720 из 2000", day.Text);

        var ing = await ui.Db.DishIngredients.AsNoTracking().SingleAsync();
        Assert.Equal(100, ing.Grams, 3);
    }

    [Fact]
    public async Task Diary_LogsProductAndShowsTotalsAndMacros()
    {
        using var ui = new Ui();
        int rice = await ui.Id("Рис");

        var day = await ui.Tap(Alice, $"l:g:{rice}:100");
        Assert.Contains("360 из 2000", day.Text);
        Assert.Contains("Белки: 7 из 100", day.Text);
        Assert.Contains("Осталось: 1640", day.Text);
    }

    [Fact]
    public async Task Diary_EatDishPortions_GoalAndDelete()
    {
        using var ui = new Ui();
        await SaveDish(ui, "Каша", ("Рис", 100));
        int dishId = (await ui.Db.Dishes.FirstAsync()).Id;

        var day = await ui.Tap(Alice, $"d:ate:{dishId}:1.5");
        Assert.Contains("540 из 2000", day.Text);

        day = await ui.Tap(Alice, "t:g:1500");
        Assert.Contains("540 из 1500", day.Text);

        int entryId = (await ui.Db.DiaryEntries.AsNoTracking().SingleAsync()).Id;
        day = await ui.Tap(Alice, $"t:del:{entryId}");
        Assert.Contains("Пока ничего не записано", day.Text);
    }

    [Fact]
    public async Task Diary_IsPerMemberAndPerDay()
    {
        using var ui = new Ui();
        int rice = await ui.Id("Рис");
        await ui.Tap(Alice, $"l:g:{rice}:100");

        var bob = await ui.Msg(Bob, "📅 Дневник");
        Assert.Contains("Пока ничего не записано", bob.Text);

        ui.Today = ui.Today.AddDays(1);
        var next = await ui.Msg(Alice, "📅 Дневник");
        Assert.Contains("Пока ничего не записано", next.Text);
        var prev = await ui.Tap(Alice, "t:prev");
        Assert.Contains("Вчера", prev.Text);
        Assert.Contains("360 из", prev.Text);
    }

    [Fact]
    public async Task Diary_TypedGoalIsValidated()
    {
        using var ui = new Ui();
        await ui.Tap(Alice, "t:gx");
        var bad = await ui.Msg(Alice, "50");
        Assert.Contains("от 800 до 6000", bad.Text);
        var ok = await ui.Msg(Alice, "2100");
        Assert.Contains("0 из 2100", ok.Text);
    }

    [Fact]
    public void SchemaUpgrade_CreatesDiaryTablesInOldDatabase()
    {
        using var fx = new BotFixture();
        fx.Db.Database.ExecuteSqlRaw("DROP TABLE \"DiaryEntries\"");
        fx.Db.Database.ExecuteSqlRaw("DROP TABLE \"MemberTargets\"");

        AppDbContext.Initialize(fx.Db);

        fx.Db.DiaryEntries.Add(new DiaryEntry { MemberId = 1, Date = new DateOnly(2026, 11, 23), Label = "x", Kcal = 1 });
        fx.Db.SaveChanges();
        Assert.Equal(1, fx.Db.DiaryEntries.Count());
    }
}
