namespace MealPlanner.Core.Models;

/// <summary>
/// Nutrient vector: calories and macros (protein, fat, carbohydrates)
/// </summary>
public readonly record struct Nutrients(double Kcal, double Protein, double Fat, double Carbs)
{
    public static Nutrients Zero => new(0, 0, 0, 0);

    public static Nutrients operator +(Nutrients a, Nutrients b) =>
        new(a.Kcal + b.Kcal, a.Protein + b.Protein, a.Fat + b.Fat, a.Carbs + b.Carbs);

    public static Nutrients operator *(Nutrients a, double k) =>
        new(a.Kcal * k, a.Protein * k, a.Fat * k, a.Carbs * k);

    public double this[int index] => index switch
    {
        0 => Kcal,
        1 => Protein,
        2 => Fat,
        3 => Carbs,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    public static readonly string[] Names = { "Kcal", "Protein", "Fat", "Carbs" };
}
