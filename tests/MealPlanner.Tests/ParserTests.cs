using MealPlanner.Core.Data;
using MealPlanner.Core.Parsing;
using Xunit;

namespace MealPlanner.Tests;

public class ParserTests
{
    private static readonly IReadOnlyList<MealPlanner.Core.Models.Food> Foods = FoodCatalog.LoadSeed();

    [Fact]
    public void ParsesDishWithServings()
    {
        var d = DishParser.Parse("борщ: свёкла 200, картошка 300, говядина 150; 4 порции", Foods);

        Assert.True(d.IsValid, string.Join("; ", d.Errors.Concat(d.UnknownFoods)));
        Assert.Equal("борщ", d.Name);
        Assert.Equal(4, d.Servings);
        Assert.Equal(3, d.Ingredients.Count);
        Assert.Equal("Картофель", d.Ingredients[1].Food.Name);
    }

    [Fact]
    public void UnderstandsKilogramsPiecesAndDecimalComma()
    {
        var d = DishParser.Parse("омлет: яйца 3 шт, молоко 0,2 кг", Foods);

        Assert.True(d.IsValid);
        Assert.Equal(165, d.Ingredients[0].Grams, 6);
        Assert.Equal(200, d.Ingredients[1].Grams, 6);
    }

    [Fact]
    public void ReportsUnknownFood()
    {
        var d = DishParser.Parse("суп: драконий фрукт 100", Foods);

        Assert.False(d.IsValid);
        Assert.Contains("драконий фрукт", d.UnknownFoods);
    }

    [Fact]
    public void ReportsMissingColon()
    {
        var d = DishParser.Parse("просто текст", Foods);
        Assert.False(d.IsValid);
        Assert.NotEmpty(d.Errors);
    }

    [Theory]
    [InlineData("помидор", "Помидоры")]
    [InlineData("курица", "Куриная грудка")]
    [InlineData("лук", "Лук репчатый")]
    [InlineData("говяжий фарш", "Фарш говяжий")]
    [InlineData("Свёкла", "Свёкла")]
    public void MatchesFoodNames(string query, string expected)
    {
        Assert.Equal(expected, FoodMatcher.Find(query, Foods)?.Name);
    }
}

public class TemplateDishTests
{
    [Fact]
    public void DishCatalog_ResolvesEveryProduct()
    {
        var foods = MealPlanner.Core.Data.FoodCatalog.LoadFull();

        Assert.Empty(MealPlanner.Core.Data.DishCatalog.UnresolvedProducts(foods));
        Assert.Equal(MealPlanner.Core.Data.DishCatalog.Count, MealPlanner.Core.Data.DishCatalog.Build(foods).Count);
    }

    [Fact]
    public void DishCatalog_IsLargeAndConsistent()
    {
        var foods = MealPlanner.Core.Data.FoodCatalog.LoadFull();
        var dishes = MealPlanner.Core.Data.DishCatalog.Build(foods);

        Assert.True(dishes.Count >= 350);
        Assert.Equal(dishes.Count, dishes.Select(d => d.Dish.Name.ToLowerInvariant()).Distinct().Count());
        Assert.All(dishes, d =>
        {
            Assert.InRange(d.Dish.PerServing.Kcal, 20, 2500);
            Assert.InRange(d.Dish.CookMinutes, 1, 240);
            Assert.InRange(d.Dish.Difficulty, 1, 3);
        });
    }

    [Fact]
    public void FullFoodCatalog_HasUniqueNamesAndSequentialIds()
    {
        var foods = MealPlanner.Core.Data.FoodCatalog.LoadFull();

        Assert.True(foods.Count >= 380);
        Assert.Equal(foods.Count, foods.Select(f => f.Name.ToLowerInvariant()).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, foods.Count), foods.Select(f => f.Id));
        Assert.All(foods, f => Assert.True(f.KcalPer100 >= 0 && f.MaxGrams > 0));
    }

    [Fact]
    public void ParseItems_ReadsPantryList()
    {
        var foods = MealPlanner.Core.Data.FoodCatalog.LoadSeed();
        var r = DishParser.ParseItems("курица 500, рис 0,3 кг", foods);

        Assert.Empty(r.Errors);
        Assert.Empty(r.UnknownFoods);
        Assert.Equal(2, r.Items.Count);
        Assert.Equal(300, r.Items[1].Grams, 6);
    }

    [Fact]
    public void TargetsForKcal_MatchDefaultAt2000()
    {
        var t = MealPlanner.Core.Models.NutrientTargets.ForKcal(2000);
        Assert.Equal(75, t.ProteinMin, 6);
        Assert.Equal(150, t.ProteinMax, 6);
    }
}
