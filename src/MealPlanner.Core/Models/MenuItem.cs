namespace MealPlanner.Core.Models;

/// <summary>
/// Menu item: product and portion mass in grams
/// </summary>
public record MenuItem(Food Food, double Grams)
{
    public Nutrients Nutrients => Food.NutrientsFor(Grams);
    public double Cost => Food.CostFor(Grams);
}
