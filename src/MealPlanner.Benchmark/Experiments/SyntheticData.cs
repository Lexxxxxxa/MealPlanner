using MealPlanner.Core.Models;

namespace MealPlanner.Benchmark.Experiments;

/// <summary>
/// Deterministic generator of synthetic dishes and fridges for experiments
/// </summary>
public static class SyntheticData
{
    public static List<Dish> GenerateDishes(IReadOnlyList<Food> foods, int count, int seed)
    {
        var rnd = new Random(seed);
        var grains = foods.Where(f => f.Category == "Крупы и хлеб").ToList();
        var potato = foods.Where(f => f.Name == "Картофель").ToList();
        var veg = foods.Where(f => f.Category == "Овощи" && f.Name != "Картофель").ToList();
        var meat = foods.Where(f => f.Category == "Мясо и рыба").ToList();
        var legumes = foods.Where(f => f.Category == "Бобовые").ToList();
        var dairy = foods.Where(f => f.Category == "Молочные и яйца").ToList();
        var fats = foods.Where(f => f.Category == "Жиры и орехи").ToList();

        T Pick<T>(IReadOnlyList<T> list) => list[rnd.Next(list.Count)];
        double Range(double a, double b) => a + rnd.NextDouble() * (b - a);

        var dishes = new List<Dish>(count);
        for (int i = 0; i < count; i++)
        {
            int servings = rnd.Next(1, 5);
            var used = new HashSet<int>();
            var items = new List<DishIngredient>();

            void Add(Food f, double perServing)
            {
                if (!used.Add(f.Id)) return;
                items.Add(new DishIngredient(f, Math.Round(perServing * servings / 5.0) * 5.0 + 5.0));
            }

            Add(rnd.NextDouble() < 0.8 ? Pick(grains) : Pick(potato), Range(50, 120));

            double p = rnd.NextDouble();
            Add(p < 0.6 ? Pick(meat) : p < 0.8 ? Pick(legumes) : Pick(dairy), Range(60, 150));

            int vegCount = rnd.Next(1, 4);
            for (int v = 0; v < vegCount; v++) Add(Pick(veg), Range(40, 150));

            if (rnd.NextDouble() < 0.6) Add(Pick(fats), Range(5, 15));
            if (rnd.NextDouble() < 0.2) Add(Pick(dairy), Range(15, 40));

            dishes.Add(new Dish($"Блюдо {i + 1}", servings, items));
        }
        return dishes;
    }

    public static Dictionary<int, double> GeneratePantry(
        IReadOnlyList<Food> foods, IReadOnlyList<Dish> dishes, Random rnd, double density)
    {
        var pantry = new Dictionary<int, double>();

        int planned = rnd.Next(2, 5);
        for (int i = 0; i < planned; i++)
        {
            var dish = dishes[rnd.Next(dishes.Count)];
            foreach (var ing in dish.Ingredients)
            {
                double have = ing.Grams * (1.0 + rnd.NextDouble() * 0.5);
                pantry[ing.Food.Id] = Math.Max(pantry.GetValueOrDefault(ing.Food.Id), have);
            }
        }

        foreach (var f in foods)
        {
            if (rnd.NextDouble() < density)
            {
                double have = 50 + rnd.NextDouble() * 550;
                pantry[f.Id] = Math.Max(pantry.GetValueOrDefault(f.Id), have);
            }
        }
        return pantry;
    }
}
