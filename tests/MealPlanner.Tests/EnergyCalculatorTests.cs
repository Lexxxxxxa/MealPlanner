using MealPlanner.Core.Models;
using Xunit;

namespace MealPlanner.Tests;

public class EnergyCalculatorTests
{
    [Fact]
    public void Bmi_And_Category()
    {
        double bmi = EnergyCalculator.Bmi(80, 180);
        Assert.Equal(24.69, bmi, 2);
        Assert.Equal("норма", EnergyCalculator.BmiCategory(bmi));
        Assert.Equal("недостаточный вес", EnergyCalculator.BmiCategory(17));
        Assert.Equal("избыточный вес", EnergyCalculator.BmiCategory(27));
        Assert.Equal("ожирение", EnergyCalculator.BmiCategory(32));
    }

    [Fact]
    public void Bmr_MifflinStJeor()
    {
        Assert.Equal(1780, EnergyCalculator.Bmr(Sex.Male, 30, 180, 80), 6);
        Assert.Equal(1780 - 166, EnergyCalculator.Bmr(Sex.Female, 30, 180, 80), 6);
    }

    [Theory]
    [InlineData(DietGoal.Lose, 2080)]
    [InlineData(DietGoal.Maintain, 2450)]
    [InlineData(DietGoal.Gain, 2690)]
    public void DailyTarget_DependsOnGoal(DietGoal goal, double expected) =>
        Assert.Equal(expected, EnergyCalculator.DailyTarget(Sex.Male, 30, 180, 80, 1.375, goal));

    [Fact]
    public void DailyTarget_LosingNeverBelowSafeMinimum()
    {
        Assert.Equal(1200, EnergyCalculator.DailyTarget(Sex.Female, 60, 150, 45, 1.2, DietGoal.Lose));
    }

    [Fact]
    public void MacroShares_SumToOne()
    {
        foreach (var g in Enum.GetValues<DietGoal>())
        {
            var (p, f, c) = EnergyCalculator.MacroShares(g);
            Assert.Equal(1.0, p + f + c, 9);
        }
    }
}
