using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using Xunit;

namespace MealPlanner.Tests;

public class InstructionTextTests
{
    [Fact]
    public void Normalize_StripsNumberingAndKeepsOneStepPerLine()
    {
        var text = InstructionText.Normalize("1. Нарежьте\n2) Обжарьте\n- Подавайте");
        Assert.Equal("Нарежьте\nОбжарьте\nПодавайте", text);
    }

    [Fact]
    public void Format_NumbersSteps()
    {
        Assert.Equal("1. А\n2. Б", InstructionText.Format(new[] { "А", "Б" }));
    }

    [Fact]
    public void Catalog_DishesHaveSteps()
    {
        Assert.All(DishCatalog.Build(FoodCatalog.LoadFull()), x => Assert.NotEmpty(x.Dish.Steps));
    }
}
