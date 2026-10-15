using System.Diagnostics;
using System.Text;
using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Recommendation;

namespace MealPlanner.Benchmark.Experiments;

/// <summary>
/// Experiment 1: comparison of dish recommendation strategies by available products
/// For each synthetic household the strategy returns the top-K dishes
/// Metrics: Precision@K (share of dishes that can be cooked without shopping), HitRate@1,
/// NearCookable@K (at most one ingredient is missing), average shopping cost,
/// average relative deviation of portion calories from the target, time
/// </summary>
public static class RecommendationExperiment
{
    private const int TopK = 5;

    public static void Run(int households, int dishCount, string outDir)
    {
        var foods = FoodCatalog.LoadSeed();
        var dishes = SyntheticData.GenerateDishes(foods, dishCount, seed: 100);

        IDishRecommender[] recommenders =
        {
            new RandomRecommender(seed: 7),
            new BinaryCoverageRecommender(),
            new WeightedCoverageRecommender(),
            new SmartRecommender { Name = "Smart-noKcal", CalorieWeight = 0 },
            new SmartRecommender { Name = "Smart-noCostPenalty", CostPenalty = 0 },
            new SmartRecommender()
        };

        var raw = new StringBuilder("household,method,precision_at_k,hit_at_1,near_cookable_at_k,missing_cost,kcal_dev,time_ms\n");
        var acc = recommenders.ToDictionary(r => r.Name, _ => new Acc());

        var rnd = new Random(2024);
        foreach (var r in recommenders) r.Recommend(dishes, new Dictionary<int, double>(), new RecommendationContext(), TopK);

        for (int h = 1; h <= households; h++)
        {
            double density = 0.15 + rnd.NextDouble() * 0.3;
            var pantry = SyntheticData.GeneratePantry(foods, dishes, rnd, density);
            double target = 400 + rnd.NextDouble() * 400;
            var ctx = new RecommendationContext(target);

            foreach (var rec in recommenders)
            {
                var sw = Stopwatch.StartNew();
                var top = rec.Recommend(dishes, pantry, ctx, TopK);
                sw.Stop();

                double precision = top.Count(t => t.Coverage.IsCookable) / (double)TopK;
                double hit1 = top.Count > 0 && top[0].Coverage.IsCookable ? 1 : 0;
                double near = top.Count(t => t.Coverage.Missing.Count <= 1) / (double)TopK;
                double miss = top.Average(t => t.Coverage.MissingCost);
                double kcalDev = top.Average(t => Math.Abs(t.Dish.PerServing.Kcal - target) / target);
                double ms = sw.Elapsed.TotalMilliseconds;

                var a = acc[rec.Name];
                a.Precision.Add(precision); a.Hit1.Add(hit1); a.Near.Add(near);
                a.Missing.Add(miss); a.KcalDev.Add(kcalDev); a.Time.Add(ms);

                raw.AppendLine(string.Join(",", h, rec.Name, Stats.F(precision), Stats.F(hit1), Stats.F(near),
                    Stats.F(miss, 4), Stats.F(kcalDev, 4), Stats.F(ms, 4)));
            }
        }

        var summary = new StringBuilder(
            "method,precision_at_k,precision_ci95,hit_at_1,near_cookable_at_k,missing_cost_eur,kcal_dev,time_ms\n");
        Console.WriteLine($"\n== Експеримент 1: рекомендації ({households} домогосподарств, {dishCount} блюд, top-{TopK}) ==");
        Console.WriteLine($"{"method",-22} {"P@5",7} {"±CI95",7} {"Hit@1",7} {"Near@5",7} {"miss EUR",9} {"kcalDev",8} {"ms",8}");
        foreach (var rec in recommenders)
        {
            var a = acc[rec.Name];
            Console.WriteLine($"{rec.Name,-22} {Stats.F(Stats.Mean(a.Precision)),7} {Stats.F(Stats.Ci95(a.Precision)),7} " +
                              $"{Stats.F(Stats.Mean(a.Hit1)),7} {Stats.F(Stats.Mean(a.Near)),7} " +
                              $"{Stats.F(Stats.Mean(a.Missing), 2),9} {Stats.F(Stats.Mean(a.KcalDev)),8} {Stats.F(Stats.Mean(a.Time), 3),8}");
            summary.AppendLine(string.Join(",", rec.Name, Stats.F(Stats.Mean(a.Precision), 4), Stats.F(Stats.Ci95(a.Precision), 4),
                Stats.F(Stats.Mean(a.Hit1), 4), Stats.F(Stats.Mean(a.Near), 4), Stats.F(Stats.Mean(a.Missing), 4),
                Stats.F(Stats.Mean(a.KcalDev), 4), Stats.F(Stats.Mean(a.Time), 4)));
        }

        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "exp1_recommend_raw.csv"), raw.ToString());
        File.WriteAllText(Path.Combine(outDir, "exp1_recommend_summary.csv"), summary.ToString());
    }

    private class Acc
    {
        public List<double> Precision { get; } = new();
        public List<double> Hit1 { get; } = new();
        public List<double> Near { get; } = new();
        public List<double> Missing { get; } = new();
        public List<double> KcalDev { get; } = new();
        public List<double> Time { get; } = new();
    }
}
