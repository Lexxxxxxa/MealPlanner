namespace MealPlanner.Benchmark.Experiments;

public static class Stats
{
    public static double Mean(IReadOnlyCollection<double> v) => v.Count == 0 ? double.NaN : v.Average();

    public static double StdDev(IReadOnlyCollection<double> v)
    {
        if (v.Count < 2) return 0;
        double m = v.Average();
        return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / (v.Count - 1));
    }

    public static double Ci95(IReadOnlyCollection<double> v) =>
        v.Count < 2 ? 0 : 1.96 * StdDev(v) / Math.Sqrt(v.Count);

    public static string F(double x, int digits = 3) =>
        x.ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture);
}
