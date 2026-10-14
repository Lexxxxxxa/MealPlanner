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
