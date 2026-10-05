namespace MealPlanner.Core.Optimization;

public enum Relation { LessOrEqual, GreaterOrEqual, Equal }

public enum LpStatus { Optimal, Infeasible, Unbounded, IterationLimit }

/// <summary>
/// Linear constraint: sum of Coefficients[j]*x[j] (Relation) Rhs
/// </summary>
public record LinearConstraint(double[] Coefficients, Relation Relation, double Rhs);

public record LpResult(LpStatus Status, double[] X, double Objective, int Iterations);

/// <summary>
/// Two-phase simplex method (tableau variant) for the problem
/// min c^T x subject to Ax (<= | >= | =) b, x >= 0
/// Entering variable rule is Dantzig, on degeneracy (cycling) it switches to Bland's rule
/// </summary>
public class SimplexSolver
{
    private const double Eps = 1e-9;
    private const double PhaseOneTolerance = 1e-7;
    private const int DegenerateRunBeforeBland = 50;

    public int MaxIterations { get; init; } = 200_000;

    public LpResult Minimize(double[] c, IReadOnlyList<LinearConstraint> constraints)
    {
        int n = c.Length;
        int m = constraints.Count;

        var a = new double[m][];
        var b = new double[m];
        var rel = new Relation[m];
        for (int i = 0; i < m; i++)
        {
            var con = constraints[i];
            a[i] = (double[])con.Coefficients.Clone();
            b[i] = con.Rhs;
            rel[i] = con.Relation;
            if (b[i] < 0)
            {
                for (int j = 0; j < n; j++) a[i][j] = -a[i][j];
                b[i] = -b[i];
                rel[i] = rel[i] switch
                {
                    Relation.LessOrEqual => Relation.GreaterOrEqual,
                    Relation.GreaterOrEqual => Relation.LessOrEqual,
                    _ => Relation.Equal
                };
            }
        }

        int slackCount = rel.Count(r => r != Relation.Equal);
        int artCount = rel.Count(r => r != Relation.LessOrEqual);
        int total = n + slackCount + artCount;
        int rhs = total;
        int firstArt = n + slackCount;

        var t = new double[m + 1][];
        for (int i = 0; i <= m; i++) t[i] = new double[total + 1];
        var basis = new int[m];

        int sIdx = n;
        int aIdx = firstArt;
        for (int i = 0; i < m; i++)
        {
            Array.Copy(a[i], t[i], n);
            t[i][rhs] = b[i];
            switch (rel[i])
            {
                case Relation.LessOrEqual:
                    t[i][sIdx] = 1; basis[i] = sIdx; sIdx++;
                    break;
                case Relation.GreaterOrEqual:
                    t[i][sIdx] = -1; sIdx++;
                    t[i][aIdx] = 1; basis[i] = aIdx; aIdx++;
                    break;
                default:
                    t[i][aIdx] = 1; basis[i] = aIdx; aIdx++;
                    break;
            }
        }

        int iterations = 0;

        if (artCount > 0)
        {
            for (int j = firstArt; j < total; j++) t[m][j] = 1;
            for (int i = 0; i < m; i++)
            {
                if (basis[i] < firstArt) continue;
                for (int j = 0; j <= total; j++) t[m][j] -= t[i][j];
            }

            var status = Iterate(t, basis, m, total, rhs, ref iterations);
            if (status == LpStatus.IterationLimit)
                return new LpResult(status, new double[n], double.NaN, iterations);
            if (-t[m][rhs] > PhaseOneTolerance)
                return new LpResult(LpStatus.Infeasible, new double[n], double.NaN, iterations);

            for (int i = 0; i < m; i++)
            {
                if (basis[i] < firstArt) continue;
                for (int j = 0; j < firstArt; j++)
                {
                    if (Math.Abs(t[i][j]) > Eps)
                    {
                        Pivot(t, m, total, i, j);
                        basis[i] = j;
                        break;
                    }
                }
            }
        }

        for (int j = 0; j <= total; j++) t[m][j] = 0;
        for (int j = 0; j < n; j++) t[m][j] = c[j];
        for (int i = 0; i < m; i++)
        {
            double cb = basis[i] < n ? c[basis[i]] : 0;
            if (cb == 0) continue;
            for (int j = 0; j <= total; j++) t[m][j] -= cb * t[i][j];
        }

        var st = Iterate(t, basis, m, firstArt, rhs, ref iterations);

        var x = new double[n];
        for (int i = 0; i < m; i++)
            if (basis[i] < n) x[basis[i]] = t[i][rhs];

        double obj = 0;
        for (int j = 0; j < n; j++) obj += c[j] * x[j];

        return new LpResult(st, x, st == LpStatus.Optimal ? obj : double.NaN, iterations);
    }

    private LpStatus Iterate(double[][] t, int[] basis, int m, int colLimit, int rhs, ref int iterations)
    {
        int degenerateRun = 0;
        while (true)
        {
            if (iterations++ > MaxIterations) return LpStatus.IterationLimit;

            bool bland = degenerateRun > DegenerateRunBeforeBland;
            int enter = -1;
            double bestVal = -Eps;
            for (int j = 0; j < colLimit; j++)
            {
                double r = t[m][j];
                if (r < -Eps)
                {
                    if (bland) { enter = j; break; }
                    if (r < bestVal) { bestVal = r; enter = j; }
                }
            }
            if (enter < 0) return LpStatus.Optimal;

            int leave = -1;
            double bestRatio = double.PositiveInfinity;
            for (int i = 0; i < m; i++)
            {
                double aij = t[i][enter];
                if (aij <= Eps) continue;
                double ratio = t[i][rhs] / aij;
                if (ratio < bestRatio - 1e-12 ||
                    (Math.Abs(ratio - bestRatio) <= 1e-12 && leave >= 0 && basis[i] < basis[leave]))
                {
                    bestRatio = ratio;
                    leave = i;
                }
            }
            if (leave < 0) return LpStatus.Unbounded;

            degenerateRun = bestRatio < 1e-12 ? degenerateRun + 1 : 0;
            Pivot(t, m, t[0].Length - 1, leave, enter);
            basis[leave] = enter;
        }
    }

    private static void Pivot(double[][] t, int m, int total, int row, int col)
    {
        double[] pr = t[row];
        double p = pr[col];
        for (int j = 0; j <= total; j++) pr[j] /= p;

        for (int i = 0; i <= m; i++)
        {
            if (i == row) continue;
            double f = t[i][col];
            if (Math.Abs(f) < 1e-15) continue;
            double[] ri = t[i];
            for (int j = 0; j <= total; j++) ri[j] -= f * pr[j];
            ri[col] = 0;
        }
    }
}
