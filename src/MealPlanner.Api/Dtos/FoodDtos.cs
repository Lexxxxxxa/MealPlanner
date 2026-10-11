namespace MealPlanner.Api.Dtos;

public record FoodInput(
    string Name, string Category,
    double KcalPer100, double ProteinPer100, double FatPer100, double CarbsPer100,
    double PricePer100, double MaxGrams = 400);
