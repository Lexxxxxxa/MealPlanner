# MealPlanner

[English](README.md) | [Deutsch](README.de.md) | [Русский](README.ru.md)

A Telegram bot for a couple (or family): a shared dish list with calorie and macronutrient (protein, fat, carbs) counting, pantry tracking,
"what can I cook with what I have" suggestions and a daily menu. For a course project it also contains an experimental part:
comparison of recommendation strategies, ingredient substitution and menu planning from limited stock.

> The bot interface is in Ukrainian. Button labels below are quoted as they appear in the bot, with a translation.

## Structure

| Project | Purpose |
|---|---|
| `src/MealPlanner.Core` | Models, dish text parsing, menu algorithms (greedy, simplex), recommendations, ingredient substitution, product catalog `Data/foods.csv` |
| `src/MealPlanner.App` | EF Core + SQLite (households, dishes), `BotUi` (button interface) and `CommandHandler` (text commands), independent of Telegram |
| `src/MealPlanner.BotHost` | Console host for the Telegram bot (long polling) |
| `src/MealPlanner.Benchmark` | Experiments 0–3, results in `results/*.csv` |
| `tests/MealPlanner.Tests` | xUnit tests (parser, algorithms, bot commands on in-memory SQLite) |

## Getting started

Requires the .NET 8 SDK.

```bash
dotnet build
dotnet test
```

### Bot

1. Create a bot in Telegram via `@BotFather` and copy the token.
2. Find your own and your partner's Telegram ID (for example via `@userinfobot`).
3. Run (Windows PowerShell):

```powershell
$env:TELEGRAM_BOT_TOKEN = "123456:ABC..."
$env:ALLOWED_USER_IDS = "11111111,22222222"
dotnet run --project src/MealPlanner.BotHost
```

Do not store the token in the repository. `ALLOWED_USER_IDS` restricts access to the two of you.
In the chat: `/start`, then your partner sends `/join CODE`.

The whole interface uses buttons (menu at the bottom of the screen):

- **🍳 Що приготувати** (What to cook): mark the products you have (categories → products) or type them as a list; the bot picks dishes from your database and ideas from the built-in ones.
- **📖 Мої блюда** (My dishes): list with calories, ingredients, deletion. On a dish card:
  - **✏️ Змінити** (Edit): change, add or remove products, grams (buttons −50/−10/+10/+50 or a number), number of servings; "Зберегти" (Save) updates the recipe.
  - **👩‍🍳 Готую** (Cooking): the same, but with actual grams: the bot shows actual calories and macros against the recipe (difference per serving); you can log a serving in the diary or update the recipe.
  - **🍽 З'їв(ла)** (Eaten): log ½, 1, 1½, 2 or 3 servings in the diary.
- **📝 Рецепт** (Recipe): every dish has a cooking time and steps ("Як готувати", How to cook). The buttons "📝 Рецепт" and "🗑 Прибрати рецепт" (Remove recipe) on a dish card let you add or change the steps; in cooking mode the steps are shown next to the grams. Steps for built-in dishes are generated from templates by dish type. They are drafts and should be reviewed and corrected.
- **⏱ Time and difficulty:** when adding a dish, after the servings you choose the cooking time (10–90 min) and difficulty (easy / medium / hard); both can be changed under "Змінити". In "Що приготувати" the filters "⚡ До 20 мин" (up to 20 min) and "🔧 Складність" (difficulty: any → not hard → easy only) narrow down the options.
- **💡 Що з'їсти до норми** (What to eat to reach the target, on the day screen): the bot takes today's remaining calories and macros and picks servings (½, 1, 1½, 2) of your dishes and built-in ideas that fill the remainder best. Score = |Δkcal| / remaining kcal + Σ over protein, fat, carbs (0.5 · excess + 0.25 · shortfall) / max(remaining, 10); options above 110 % of the remaining calories are discarded. A serving can be logged directly from the card.
- **📅 Щоденник** (Diary): daily total of calories and macros against a personal target (2000 kcal by default), progress bar, adding a product in grams, deleting entries, viewing previous days, button "🎯 Моя ціль" (My target). Each member has their own diary; nutrition values are stored as a snapshot, so changing a recipe does not rewrite history.
- **🛒 Shopping mode** in "Що приготувати": if no dish can be made from the selected products, the bot shows dishes that use them and what you need to buy; the toggle "Лише з моїх продуктів" (Only my products) restores strict matching.
- **📚 База блюд** (Dish catalog): about 390 built-in dishes in 15 categories (breakfast, soups, salads, meat, poultry, fish, vegetables, porridge, pasta, legumes, baking, desserts, snacks, drinks, quick meals). Paginated browsing by category, search by name or product ("форель", "творог яйца"), filters "up to 20 min" and by difficulty, a "🎲 Випадкове" (Random) button, one-tap saving to "Мої блюда" and logging a serving.
- **🔎 Product search** is available wherever products are selected (ideas, adding or editing a dish, diary); product lists show 12 per page. The catalog holds 392 products in 25 categories (59 initial plus an extended set).
- **➕ Додати блюдо** (Add dish): name → products via buttons → grams via buttons (or a number / "3 шт" for pieces) → servings → time → difficulty → instructions (text, one step per line, or "Пропустити" to skip).
- **👫 Пара** (Couple): household code for a shared dish database.

**🎯 Моя ціль** (My target) is a 6-step wizard (sex → age → height → weight → activity → goal). Each step shows an estimate of the daily target that gets more precise as you enter data; activity and goal buttons show the corresponding value. Calculation: BMI (with WHO category), basal metabolic rate by Mifflin–St Jeor, activity multiplier (1.2–1.725) and a goal adjustment: lose weight −15 % (not below 1200/1500 kcal), maintain 0 %, gain +10 %. Macro shares depend on the goal (protein/fat/carbs: lose 25/30/45, maintain 20/30/50, gain 20/25/55). The target can also be set manually. Results are estimates and do not replace medical advice.

An existing database is automatically extended with the diary tables (`DiaryEntries`, `MemberTargets`) on startup.

Pantry stock is not persisted: products are specified only when asking for an idea.
Text commands (`/dish`, `/have`, `/cook`, `/menu` …) remain as a hidden fallback mode.

### Application architecture

- `Core`: domain logic with no dependency on Telegram or the database (models, recommenders, `PortionFit`, `EnergyCalculator`, `InstructionText`); strategies `IDishRecommender`, `IMenuPlanner`.
- `App/Services`: services and repositories behind interfaces (`IHouseholdService`, `IFoodRepository`, `IDishRepository`, `IPantryService`, `IDiaryService`), composed by `AppServices.Create`. Database initialisation: `DatabaseInitializer`.
- `App/Bot/Features`: one class per interface section (`IBotFeature`: callback prefixes and session steps); `BotUi` only routes, so a new feature is added without changing existing ones. Shared helpers: `UiKit`. Text commands: registry in `CommandHandler`.

### Running the experiments

```bash
dotnet run -c Release --project src/MealPlanner.Benchmark -- all        # or menu | recommend | substitute | pantry
```

## Experiments

0. **Menu from the full catalog:** greedy algorithm vs. LP (simplex) on 59–1000 products.
1. **Recommendations:** Random, BinaryCoverage, WeightedCoverage and Smart (IDF weighting, penalty for extra purchases, calorie matching) on synthetic households; Precision@5, Hit@1, calorie deviation.
2. **Ingredient substitution:** Random, Macro, CategoryMacro, Oracle; error in the dish's calories and macros.
3. **Menu from stock:** share of feasible menus and their cost depending on the number of products in stock.

## Mathematical formulation of the menu ("diet problem")

```
min   Σ pᵢ·xᵢ                              (cost)
s.t.  minₖ ≤ Σ aᵢₖ·xᵢ ≤ maxₖ,  k = kcal, protein, fat, carbs
      0 ≤ xᵢ ≤ min(Mᵢ, stockᵢ)/100
```

## Catalog data

`foods.csv` is the base set of 59 products (used for the experiments); `foods_extra.csv` adds 333 products; `dishes.txt` holds about 390 dishes in the format `category|name|minutes|difficulty|servings|product:grams;…`. Nutrition values per 100 g and prices are approximate, compiled from standard food composition tables; verify them against official sources (USDA FoodData Central, Skurikhin tables) before using them in research. On startup the bot automatically adds products missing from the database, so updates do not require recreating it. The test `DishCatalog_ResolvesEveryProduct` ensures that every ingredient of every dish exists in the catalog.

## Notes on the data

Nutrition values and prices in `foods.csv` are approximate reference values for study purposes, not official data.
Experiments 0–3 run on synthetic data (dishes and "fridges" are generated deterministically),
so the conclusions apply to the model, not to real user behaviour.
