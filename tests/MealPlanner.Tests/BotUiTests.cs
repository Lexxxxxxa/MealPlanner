using MealPlanner.App.Bot;
using MealPlanner.App.Data;
using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
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

    private static Task SaveDish(Ui ui, string name, params (string food, int grams)[] items) =>
        SaveDish(ui, name, 30, 2, items);

    private static async Task SaveDish(Ui ui, string name, int minutes, int difficulty, params (string food, int grams)[] items)
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
        await ui.Tap(Alice, $"a:tm:{minutes}");
        await ui.Tap(Alice, $"a:df:{difficulty}");
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

        var time = await ui.Tap(Alice, "a:s:1");
        Assert.True(HasButton(time, "a:tm:20"));
        var difficulty = await ui.Tap(Alice, "a:tm:20");
        Assert.True(HasButton(difficulty, "a:df:1"));

        var saved = await ui.Tap(Alice, "a:df:1");
        Assert.Contains("сохранено", saved.Text);
        Assert.Contains("20 мин", saved.Text);
        Assert.Contains("легко", saved.Text);
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

    [Fact]
    public async Task GoalWizard_ShowsEstimateAtEveryStep_AndSavesNorm()
    {
        using var ui = new Ui();
        var r = await ui.Tap(Alice, "g:start");
        Assert.Contains("Ориентир на день", r.Text);

        r = await ui.Tap(Alice, "g:sx:M");
        Assert.Contains("ккал", r.Text);
        r = await ui.Msg(Alice, "30");
        Assert.Contains("ккал", r.Text);
        r = await ui.Msg(Alice, "180");
        Assert.Contains("ккал", r.Text);
        r = await ui.Msg(Alice, "80");
        Assert.Contains("ИМТ: 24,7", r.Text.Replace('.', ','));
        Assert.True(HasButton(r, "g:ac:1.375"));

        r = await ui.Tap(Alice, "g:ac:1.375");
        Assert.Contains("Похудеть · 2080 ккал", r.Text + string.Join("|", r.Buttons!.SelectMany(x => x).Select(b => b.Text)));

        r = await ui.Tap(Alice, "g:go:0");
        Assert.Contains("Норма на день: 2080", r.Text);

        var day = await ui.Msg(Alice, "📅 Дневник");
        Assert.Contains("0 из 2080", day.Text);
        Assert.Contains("Похудеть", day.Text);
        Assert.Contains("Белки: 0 из 130", day.Text);
    }

    [Fact]
    public async Task GoalWizard_ValidatesTypedNumbers()
    {
        using var ui = new Ui();
        await ui.Tap(Alice, "g:start");
        await ui.Tap(Alice, "g:sx:F");
        Assert.Contains("от 14 до 100", (await ui.Msg(Alice, "5")).Text);
        await ui.Msg(Alice, "25");
        Assert.Contains("от 120 до 230", (await ui.Msg(Alice, "50")).Text);
        await ui.Msg(Alice, "165");
        Assert.Contains("от 30 до 300", (await ui.Msg(Alice, "abc")).Text);
    }

    [Fact]
    public async Task EditDish_ChangesTimeAndDifficulty()
    {
        using var ui = new Ui();
        await SaveDish(ui, "Каша", ("Рис", 100));
        int dishId = (await ui.Db.Dishes.FirstAsync()).Id;

        await ui.Tap(Alice, $"d:edit:{dishId}");
        await ui.Tap(Alice, "e:tmv:15");
        await ui.Tap(Alice, "e:dfv:1");
        await ui.Tap(Alice, "e:save");

        var dish = await ui.Db.Dishes.AsNoTracking().SingleAsync();
        Assert.Equal(15, dish.CookMinutes);
        Assert.Equal(1, dish.Difficulty);
    }

    [Fact]
    public async Task IdeaFilters_QuickOnlyHidesSlowDishes()
    {
        using var ui = new Ui();
        await SaveDish(ui, "Быстрая яичница", 10, 1, ("Яйца", 165));
        await SaveDish(ui, "Долгая запечённая яичница", 90, 3, ("Яйца", 330));
        int egg = await ui.Id("Яйца");

        await ui.Msg(Alice, "🍳 Что приготовить");
        await ui.Tap(Alice, $"i:t:{egg}:0");
        var all = await ui.Tap(Alice, "i:done");
        Assert.Contains("Быстрая яичница", all.Text);
        Assert.Contains("Долгая запечённая яичница", all.Text);

        var quick = await ui.Tap(Alice, "i:f:t");
        Assert.Contains("Быстрая яичница", quick.Text);
        Assert.DoesNotContain("Долгая запечённая яичница", quick.Text);
        Assert.Contains("до 20 минут", quick.Text);

        await ui.Tap(Alice, "i:f:t");
        var easy = await ui.Tap(Alice, "i:f:d");
        easy = await ui.Tap(Alice, "i:f:d");
        Assert.Contains("только лёгкие", easy.Text);
        Assert.DoesNotContain("Долгая запечённая яичница", easy.Text);
    }

    [Fact]
    public async Task FitToNorm_SuggestsDishesThatFitRemainder()
    {
        using var ui = new Ui();
        await SaveDish(ui, "Лёгкий обед", ("Куриная грудка", 150), ("Рис", 60));
        await SaveDish(ui, "Огромная порция риса", ("Рис", 1000));
        int rice = await ui.Id("Рис");

        await ui.Tap(Alice, $"l:g:{rice}:300");
        var day = await ui.Msg(Alice, "📅 Дневник");
        Assert.True(HasButton(day, "t:fit"));

        var fit = await ui.Tap(Alice, "t:fit");
        Assert.Contains("Осталось до нормы: 920", fit.Text);
        Assert.DoesNotContain("Огромная порция риса", fit.Text);
        Assert.True(HasButton(fit, "t:fv:0"));

        var view = await ui.Tap(Alice, "t:fv:0");
        Assert.Contains("После этого останется", view.Text);

        var logged = await ui.Tap(Alice, "t:fa:0");
        Assert.Contains("Калории:", logged.Text);
        Assert.Equal(2, await ui.Db.DiaryEntries.CountAsync());
    }

    [Fact]
    public async Task FitToNorm_WhenNormReached_SaysSo()
    {
        using var ui = new Ui();
        await ui.Tap(Alice, "t:g:800");
        int rice = await ui.Id("Рис");
        await ui.Tap(Alice, $"l:g:{rice}:220");

        var fit = await ui.Tap(Alice, "t:fit");
        Assert.Contains("Норма на сегодня выполнена", fit.Text);
    }

    [Fact]
    public void FitScore_PrefersCalorieMatchAndPenalisesOvershoot()
    {
        var remaining = new Nutrients(600, 40, 20, 70);
        var close = new Nutrients(580, 38, 18, 68);
        var heavy = new Nutrients(580, 10, 60, 40);
        Assert.True(BotUi.FitScore(close, remaining) < BotUi.FitScore(heavy, remaining));
        Assert.True(BotUi.FitScore(close, remaining) < BotUi.FitScore(new Nutrients(200, 10, 5, 25), remaining));
    }

    [Fact]
    public void SchemaUpgrade_AddsTimeAndDifficultyToOldDishesTable()
    {
        using var fx = new BotFixture();
        fx.Db.Database.ExecuteSqlRaw("ALTER TABLE \"Dishes\" RENAME TO \"DishesOld\"");
        fx.Db.Database.ExecuteSqlRaw("CREATE TABLE \"Dishes\" (\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT, \"HouseholdId\" INTEGER NOT NULL, \"Name\" TEXT NOT NULL, \"Servings\" INTEGER NOT NULL)");

        AppDbContext.Initialize(fx.Db);

        fx.Db.Database.ExecuteSqlRaw("INSERT INTO \"Dishes\" (\"HouseholdId\", \"Name\", \"Servings\") VALUES (1, 'x', 1)");
        Assert.Equal(30, fx.Db.Dishes.AsNoTracking().Single().CookMinutes);
        Assert.Equal(2, fx.Db.Dishes.AsNoTracking().Single().Difficulty);
    }

    private static string? FirstData(BotReply r, string prefix) =>
        r.Buttons?.SelectMany(row => row).FirstOrDefault(b => b.Data.StartsWith(prefix))?.Data;

    [Fact]
    public async Task Database_ContainsFullProductCatalogue()
    {
        using var ui = new Ui();

        Assert.True(await ui.Db.Foods.CountAsync() >= 380);
        Assert.True(await ui.Db.Foods.AnyAsync(f => f.Name == "Форель"));
    }

    [Fact]
    public async Task Initialize_AddsMissingProductsToExistingDatabase()
    {
        using var fx = new BotFixture();
        var trout = await fx.Db.Foods.FirstAsync(f => f.Name == "Форель");
        fx.Db.Foods.Remove(trout);
        await fx.Db.SaveChangesAsync();

        AppDbContext.Initialize(fx.Db);

        Assert.Equal(1, await fx.Db.Foods.CountAsync(f => f.Name == "Форель"));
    }

    [Fact]
    public async Task ProductLists_ArePaged()
    {
        using var ui = new Ui();
        var r = await ui.Msg(Alice, "🍳 Что приготовить");
        var veg = r.Buttons!.SelectMany(row => row).First(b => b.Text.Contains("Овощи")).Data;

        var page0 = await ui.Tap(Alice, veg);
        Assert.Contains("Страница 1 из", page0.Text);
        Assert.True(page0.Buttons!.SelectMany(row => row).Count() <= 20);
        Assert.True(HasButton(page0, veg + ":1"));
    }

    [Fact]
    public async Task IdeaSearch_FindsProductAndSelectsIt()
    {
        using var ui = new Ui();
        await ui.Msg(Alice, "🍳 Что приготовить");
        await ui.Tap(Alice, "i:find");
        var r = await ui.Msg(Alice, "форел");
        Assert.True(HasButtonText(r, "Форель"));

        var pick = FirstData(r, "i:s:");
        Assert.NotNull(pick);
        var marked = await ui.Tap(Alice, pick!);
        Assert.True(HasButtonText(marked, "✅ Форель"));
    }

    [Fact]
    public async Task Idea_WithOneProduct_OffersDishesWithShopping()
    {
        using var ui = new Ui();
        await ui.Msg(Alice, "🍳 Что приготовить");
        await ui.Tap(Alice, "i:find");
        var r = await ui.Msg(Alice, "картофел");
        var pick = FirstData(r, "i:s:");
        Assert.NotNull(pick);
        await ui.Tap(Alice, pick!);

        var res = await ui.Tap(Alice, "i:done");
        Assert.Contains("докупить", res.Text);
        Assert.NotNull(FirstData(res, "r:v:"));
        Assert.True(HasButtonText(res, "Только из моих продуктов"));

        var back = await ui.Tap(Alice, "i:m");
        Assert.DoesNotContain("докупить)", back.Text);
    }

    [Fact]
    public void CategoryLabels_HaveIcons()
    {
        foreach (var c in DishCatalog.Categories)
            Assert.DoesNotContain("🍴", BotUi.LabelFor(c));
    }

    [Fact]
    public async Task AddDishSearch_FindsProductInLargeCatalogue()
    {
        using var ui = new Ui();
        await ui.Msg(Alice, "➕ Добавить блюдо");
        await ui.Msg(Alice, "Рыба");
        await ui.Tap(Alice, "a:find");
        var r = await ui.Msg(Alice, "скумбр");

        Assert.True(HasButtonText(r, "Скумбрия"));
        Assert.True(HasButtonText(r, "Копчёная скумбрия"));
    }

    [Fact]
    public async Task DishCatalog_BrowseViewSaveAndEat()
    {
        using var ui = new Ui();
        var home = await ui.Msg(Alice, "📚 База блюд");
        Assert.Contains("База блюд", home.Text);
        Assert.True(HasButton(home, "b:c:0:0"));
        Assert.True(HasButton(home, "b:find"));

        var list = await ui.Tap(Alice, "b:c:0:0");
        var view = FirstData(list, "b:v:");
        Assert.NotNull(view);

        var card = await ui.Tap(Alice, view!);
        Assert.Contains("На порцию", card.Text);
        string n = view!.Split(':')[2];

        var saved = await ui.Tap(Alice, $"b:s:{n}");
        Assert.Contains("сохранено", saved.Text);
        Assert.Equal(1, await ui.Db.Dishes.CountAsync());

        await ui.Tap(Alice, $"b:e:{n}");
        var day = await ui.Tap(Alice, $"b:ate:{n}:1");
        Assert.Contains("Калории:", day.Text);
        Assert.Equal(1, await ui.Db.DiaryEntries.CountAsync());
    }

    [Fact]
    public async Task DishCatalog_SearchByIngredientAndName()
    {
        using var ui = new Ui();
        await ui.Msg(Alice, "📚 База блюд");
        await ui.Tap(Alice, "b:find");

        var byName = await ui.Msg(Alice, "борщ");
        Assert.True(HasButtonText(byName, "Борщ"));

        await ui.Tap(Alice, "b:find");
        var byIngredient = await ui.Msg(Alice, "форель");
        Assert.True(HasButtonText(byIngredient, "Стейк форели"));
    }

    [Fact]
    public async Task DishCatalog_FiltersHideSlowDishes()
    {
        using var ui = new Ui();
        await ui.Msg(Alice, "📚 База блюд");
        await ui.Tap(Alice, "b:find");
        var all = await ui.Msg(Alice, "борщ");
        Assert.True(HasButtonText(all, "Борщ"));

        await ui.Tap(Alice, "b:f:t");
        await ui.Tap(Alice, "b:find");
        var quick = await ui.Msg(Alice, "борщ");
        Assert.False(HasButtonText(quick, "Борщ · "));
    }

    [Fact]
    public async Task OwnDishes_SearchAndReset()
    {
        using var ui = new Ui();
        await SaveDish(ui, "Каша овсяная", ("Овсяные хлопья", 60));
        await SaveDish(ui, "Омлет", ("Яйца", 165));

        await ui.Msg(Alice, "📖 Мои блюда");
        await ui.Tap(Alice, "d:find");
        var found = await ui.Msg(Alice, "яйца");
        Assert.True(HasButtonText(found, "Омлет"));
        Assert.False(HasButtonText(found, "Каша овсяная"));
        Assert.True(HasButton(found, "d:clr"));

        var all = await ui.Tap(Alice, "d:clr");
        Assert.True(HasButtonText(all, "Каша овсяная"));
    }

    [Fact]
    public async Task MainMenu_HasCatalogAndDiaryButtons()
    {
        var keys = BotUi.MainMenu.SelectMany(r => r).ToList();

        Assert.Contains("📚 База блюд", keys);
        Assert.Contains("📅 Дневник", keys);
        await Task.CompletedTask;
    }
}
