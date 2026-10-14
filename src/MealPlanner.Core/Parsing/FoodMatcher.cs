using MealPlanner.Core.Models;

namespace MealPlanner.Core.Parsing;

/// <summary>
/// Fuzzy product search by a free-form name:
/// synonyms, normalization and word comparison by common prefix
/// </summary>
public static class FoodMatcher
{
    private const double Threshold = 0.75;

    private static readonly Dictionary<string, string> Synonyms = new()
    {
        ["картошка"] = "картофель",
        ["курица"] = "куриная грудка",
        ["курицу"] = "куриная грудка",
        ["курятина"] = "куриная грудка",
        ["индейка"] = "филе индейки",
        ["помидор"] = "помидоры",
        ["томат"] = "помидоры",
        ["томаты"] = "помидоры",
        ["огурец"] = "огурцы",
        ["лук"] = "лук репчатый",
        ["яйцо"] = "яйца",
        ["сметана"] = "сметана 15%",
        ["молоко"] = "молоко 1.5%",
        ["масло"] = "масло подсолнечное",
        ["фарш"] = "фарш говяжий",
        ["говяжий фарш"] = "фарш говяжий",
        ["творог"] = "творог обезжиренный",
        ["сыр"] = "сыр гауда",
        ["капуста"] = "капуста белокочанная",
        ["перец"] = "перец болгарский",
        ["грибы"] = "шампиньоны",
        ["горох"] = "горошек зелёный замороженный",
        ["горошек"] = "горошек зелёный замороженный",
        ["тунец"] = "тунец консервированный",
        ["колбаса"] = "колбаса варёная",
        ["хлеб"] = "хлеб цельнозерновой",
        ["мука"] = "мука пшеничная",
        ["лосось"] = "сёмга",
        ["семга"] = "сёмга",
    };

    public static string Normalize(string s) =>
        string.Join(' ', s.ToLowerInvariant().Replace('ё', 'е').Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static Food? Find(string query, IReadOnlyList<Food> foods)
    {
        string q = Normalize(query);
        if (q.Length == 0) return null;
        if (Synonyms.TryGetValue(q, out var syn)) q = Normalize(syn);

        var qTokens = q.Split(' ');
        Food? best = null;
        double bestScore = 0;

        foreach (var food in foods)
        {
            string name = Normalize(food.Name);
            if (name == q) return food;

            var fTokens = name.Split(' ');
            double sum = 0;
            foreach (var qt in qTokens)
                sum += fTokens.Max(ft => TokenSimilarity(qt, ft));
            double score = sum / qTokens.Length;

            bool better = score > bestScore + 1e-9 ||
                          (Math.Abs(score - bestScore) <= 1e-9 && best != null && food.Name.Length < best.Name.Length);
            if (better)
            {
                bestScore = score;
                best = food;
            }
        }
        return bestScore >= Threshold ? best : null;
    }

    private static double TokenSimilarity(string a, string b)
    {
        if (a == b) return 1.0;
        int min = Math.Min(a.Length, b.Length);
        if (min < 4) return 0;
        int p = 0;
        while (p < min && a[p] == b[p]) p++;
        return p >= 4 && p >= min - 2 ? 0.8 : 0;
    }
}
