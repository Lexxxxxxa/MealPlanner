namespace MealPlanner.Core.Models;

public enum Sex { Male = 0, Female = 1 }

/// <summary>
/// Nutrition goal that determines the correction of the daily norm
/// </summary>
public enum DietGoal { Lose = 0, Maintain = 1, Gain = 2 }

/// <summary>
/// Calculation of BMI and daily calorie norm: basal metabolism by Mifflin-St Jeor,
/// activity multiplier and goal correction (-15% / 0 / +10%)
/// Results are approximate and do not replace medical advice
/// </summary>
public static class EnergyCalculator
{
    public static readonly (double Factor, string Label)[] ActivityLevels =
    {
        (1.2, "Сидячий образ жизни"),
        (1.375, "Лёгкая активность (1–3 тренировки в неделю)"),
        (1.55, "Средняя (3–5 тренировок в неделю)"),
        (1.725, "Высокая (ежедневные тренировки)")
    };

    public static double Bmi(double weightKg, double heightCm)
    {
        double m = heightCm / 100.0;
        return weightKg / (m * m);
    }

    public static string BmiCategory(double bmi) => bmi switch
    {
        < 18.5 => "недостаточный вес",
        < 25 => "норма",
        < 30 => "избыточный вес",
        _ => "ожирение"
    };

    public static double Bmr(Sex sex, int age, double heightCm, double weightKg) =>
        10 * weightKg + 6.25 * heightCm - 5 * age + (sex == Sex.Male ? 5 : -161);

    public static double Tdee(Sex sex, int age, double heightCm, double weightKg, double activity) =>
        Bmr(sex, age, heightCm, weightKg) * activity;

    public static double GoalFactor(DietGoal goal) => goal switch
    {
        DietGoal.Lose => 0.85,
        DietGoal.Gain => 1.10,
        _ => 1.0
    };

    public static double DailyTarget(Sex sex, int age, double heightCm, double weightKg, double activity, DietGoal goal)
    {
        double kcal = Tdee(sex, age, heightCm, weightKg, activity) * GoalFactor(goal);
        if (goal == DietGoal.Lose) kcal = Math.Max(kcal, sex == Sex.Male ? 1500 : 1200);
        return Math.Round(kcal / 10.0) * 10;
    }

    public static (double Protein, double Fat, double Carbs) MacroShares(DietGoal goal) => goal switch
    {
        DietGoal.Lose => (0.25, 0.30, 0.45),
        DietGoal.Gain => (0.20, 0.25, 0.55),
        _ => (0.20, 0.30, 0.50)
    };

    public static string GoalLabel(DietGoal goal) => goal switch
    {
        DietGoal.Lose => "Похудеть",
        DietGoal.Gain => "Набрать вес",
        _ => "Поддерживать вес"
    };
}
