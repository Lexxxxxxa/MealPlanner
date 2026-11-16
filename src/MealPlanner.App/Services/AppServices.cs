using MealPlanner.App.Data;

namespace MealPlanner.App.Services;

/// <summary>
/// Set of application services built for one <see cref="AppDbContext"/> (composition root)
/// </summary>
public record AppServices(
    IHouseholdService Households,
    IFoodRepository Foods,
    IDishRepository Dishes,
    IPantryService Pantry,
    IDiaryService Diary)
{
    public static AppServices Create(AppDbContext db) => new(
        new HouseholdService(db),
        new FoodRepository(db),
        new DishRepository(db),
        new PantryService(db),
        new DiaryService(db));
}
