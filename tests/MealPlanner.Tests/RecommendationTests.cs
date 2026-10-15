using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Parsing;
using MealPlanner.Core.Recommendation;
using Xunit;

namespace MealPlanner.Tests;

public class RecommendationTests
{
    private static readonly IReadOnlyList<Food> Foods = FoodCatalog.LoadSeed();

    private static Dish MakeDish(string text)
    {
        var p = DishParser.Parse(text, Foods);
        Assert.True(p.IsValid, string.Join("; ", p.Errors.Concat(p.UnknownFoods)));
        return new Dish(p.Name, p.Servings, p.Ingredients);
    }

    private static Dictionary<int, double> Pantry(params (string name, double grams)[] items) =>
        items.ToDictionary(i => FoodMatcher.Find(i.name, Foods)!.Id, i => i.grams);

    [Fact]
    public void Coverage_DetectsCookableDish()
    {
        var dish = MakeDish("гречка с курицей: гречка 100, курица 200; 2 порции");
        var pantry = Pantry(("гречка", 150), ("курица", 300));

        var c = CoverageInfo.Analyze(dish, pantry);

        Assert.True(c.IsCookable);
        Assert.Equal(1.0, c.GramShare, 6);
        Assert.Empty(c.Missing);
    }

    [Fact]
    public void Coverage_ComputesMissingGramsAndCost()
    {
        var dish = MakeDish("гречка с курицей: гречка 100, курица 200; 2 порции");
        var pantry = Pantry(("гречка", 100), ("курица", 50));

        var c = CoverageInfo.Analyze(dish, pantry);

        Assert.False(c.IsCookable);
        var m = Assert.Single(c.Missing);
        Assert.Equal(150, m.MissingGrams, 6);
        Assert.Equal(1.0, c.IngredientShare, 6);
        Assert.Equal(0.5, c.GramShare, 6);
    }

    [Fact]
    public void Recommenders_PlaceCookableDishFirst()
    {
        var cookable = MakeDish("омлет: яйца 3 шт, молоко 100");
        var other = MakeDish("борщ: свёкла 200, говядина 150, капуста 200");
        var pantry = Pantry(("яйца", 300), ("молоко", 500));

        foreach (IDishRecommender r in new IDishRecommender[]
                 { new BinaryCoverageRecommender(), new WeightedCoverageRecommender(), new SmartRecommender() })
        {
            var top = r.Recommend(new[] { other, cookable }, pantry, new RecommendationContext(), 1);
            Assert.Equal("омлет", top[0].Dish.Name);
        }
    }

    [Fact]
    public void Smart_PrefersDishCloserToTargetCalories()
    {
        var light = MakeDish("салат: огурцы 150, помидоры 150");
        var heavy = MakeDish("паста: макароны 120, сыр 60, масло сливочное 20");
        var pantry = Pantry(("огурцы", 200), ("помидоры", 200), ("макароны", 200), ("сыр", 100), ("масло сливочное", 30));

        var top = new SmartRecommender().Recommend(new[] { light, heavy }, pantry,
            new RecommendationContext(TargetKcalPerServing: 600), 1);

        Assert.Equal("паста", top[0].Dish.Name);
    }

    [Fact]
    public void Substitution_PrefersSimilarFoodInSameCategory()
    {
        var finder = new SubstitutionFinder(Foods);
        var chicken = FoodMatcher.Find("курица", Foods)!;
        var candidates = Foods.Where(f => f.Id != chicken.Id).ToList();

        var best = finder.Suggest(chicken, candidates, SubstitutionMethod.CategoryMacro, 1)[0];

        Assert.Equal(chicken.Category, best.Candidate.Category);
    }
}
