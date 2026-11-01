using MealPlanner.Core.Models;

namespace MealPlanner.App.Data;

/// <summary>
/// Household: shared space for two or more members (shared dishes and pantry)
/// </summary>
public class Household
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class Member
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int HouseholdId { get; set; }
    public Household? Household { get; set; }
}

public class HouseholdDish
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Servings { get; set; } = 1;
    public List<HouseholdDishIngredient> Ingredients { get; set; } = new();
}

public class HouseholdDishIngredient
{
    public int Id { get; set; }
    public int DishId { get; set; }
    public HouseholdDish? Dish { get; set; }
    public int FoodId { get; set; }
    public Food? Food { get; set; }
    public double Grams { get; set; }
}

public class PantryEntry
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public int FoodId { get; set; }
    public Food? Food { get; set; }
    public double Grams { get; set; }
}

/// <summary>
/// Diary entry: what and how much a member ate; nutrient values are stored as a snapshot
/// </summary>
public class DiaryEntry
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public DateOnly Date { get; set; }
    public string Label { get; set; } = string.Empty;
    public double Kcal { get; set; }
    public double Protein { get; set; }
    public double Fat { get; set; }
    public double Carbs { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Member profile and daily norm: Kcal is the effective norm (calculated from the profile or set manually)
/// </summary>
public class MemberTarget
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public double Kcal { get; set; } = 2000;

    public bool HasProfile { get; set; }
    public bool IsManual { get; set; }
    public Sex Sex { get; set; } = Sex.Male;
    public int AgeYears { get; set; } = 30;
    public double HeightCm { get; set; } = 170;
    public double WeightKg { get; set; } = 70;
    public double Activity { get; set; } = 1.375;
    public DietGoal Goal { get; set; } = DietGoal.Maintain;
}
