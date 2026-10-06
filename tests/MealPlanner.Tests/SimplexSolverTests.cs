using MealPlanner.Core.Optimization;
using Xunit;

namespace MealPlanner.Tests;

public class SimplexSolverTests
{
    private static LinearConstraint Le(double rhs, params double[] a) => new(a, Relation.LessOrEqual, rhs);
    private static LinearConstraint Ge(double rhs, params double[] a) => new(a, Relation.GreaterOrEqual, rhs);
    private static LinearConstraint Eq(double rhs, params double[] a) => new(a, Relation.Equal, rhs);

    [Fact]
    public void Solves_ClassicMaximisationProblem()
    {
        var r = new SimplexSolver().Minimize(
            new[] { -3.0, -5.0 },
            new[] { Le(4, 1, 0), Le(12, 0, 2), Le(18, 3, 2) });

        Assert.Equal(LpStatus.Optimal, r.Status);
        Assert.Equal(-36, r.Objective, 6);
        Assert.Equal(2, r.X[0], 6);
        Assert.Equal(6, r.X[1], 6);
    }

    [Fact]
    public void Detects_InfeasibleProblem()
    {
        var r = new SimplexSolver().Minimize(
            new[] { 1.0 },
            new[] { Ge(5, 1), Le(3, 1) });

        Assert.Equal(LpStatus.Infeasible, r.Status);
    }

    [Fact]
    public void Detects_UnboundedProblem()
    {
        var r = new SimplexSolver().Minimize(
            new[] { -1.0 },
            new[] { Ge(1, 1) });

        Assert.Equal(LpStatus.Unbounded, r.Status);
    }

    [Fact]
    public void Handles_EqualityConstraint()
    {
        var r = new SimplexSolver().Minimize(
            new[] { 1.0, 2.0 },
            new[] { Eq(4, 1, 1) });

        Assert.Equal(LpStatus.Optimal, r.Status);
        Assert.Equal(4, r.Objective, 6);
    }

    [Fact]
    public void Handles_NegativeRightHandSide()
    {
        var r = new SimplexSolver().Minimize(
            new[] { 1.0 },
            new[] { Le(-2, -1) });

        Assert.Equal(LpStatus.Optimal, r.Status);
        Assert.Equal(2, r.Objective, 6);
    }
}
