using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Planning;
using Xunit;

namespace MealPlanner.Tests;

public class PlannerTests
{
    private static readonly NutrientTargets Targets = NutrientTargets.Default2000;

    [Fact]
    public void SeedCatalog_ContainsFoods()
    {
        var foods = FoodCatalog.LoadSeed();
        Assert.True(foods.Count >= 55);
        Assert.All(foods, f => Assert.True(f.PricePer100 > 0 && f.MaxGrams > 0));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(200)]
    public void Generator_ReturnsRequestedCount(int count)
    {
        Assert.Equal(count, FoodCatalog.Generate(count).Count);
    }

    [Fact]
    public void Greedy_FindsFeasibleMenu_AndRespectsPortionLimits()
    {
        var plan = new GreedyPlanner().Plan(FoodCatalog.LoadSeed(), Targets);

        Assert.True(plan.IsFeasible);
        Assert.All(plan.Items, i => Assert.True(i.Grams <= i.Food.MaxGrams + 1e-9));
    }

    [Fact]
    public void Lp_FindsFeasibleMenu_WithinTargets()
    {
        var plan = new LpPlanner().Plan(FoodCatalog.LoadSeed(), Targets);

        Assert.True(plan.IsFeasible);
        var t = plan.Totals;
        for (int k = 0; k < NutrientTargets.Dimensions; k++)
        {
            Assert.InRange(t[k], Targets.Min(k) - 0.5, Targets.Max(k) + 0.5);
        }
    }

    [Theory]
    [InlineData(59)]
    [InlineData(100)]
    [InlineData(200)]
    public void Lp_IsNotMoreExpensiveThanGreedy(int count)
    {
        var foods = FoodCatalog.Generate(count);
        var greedy = new GreedyPlanner().Plan(foods, Targets);
        var lp = new LpPlanner().Plan(foods, Targets);

        Assert.True(lp.IsFeasible);
        if (!greedy.IsFeasible) return;
        Assert.True(lp.TotalCost <= greedy.TotalCost + 1e-6,
            $"LP {lp.TotalCost:F4} > Greedy {greedy.TotalCost:F4}");
    }
}
