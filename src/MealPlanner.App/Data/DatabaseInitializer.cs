using MealPlanner.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.App.Data;

/// <summary>
/// Database preparation at startup: schema creation, upgrade of an existing database and adding missing catalog products
/// </summary>
public static class DatabaseInitializer
{
    private record ColumnSpec(string Table, string Definition);

    private static readonly ColumnSpec[] AddedColumns =
    {
        new("MemberTargets", "\"HasProfile\" INTEGER NOT NULL DEFAULT 0"),
        new("MemberTargets", "\"IsManual\" INTEGER NOT NULL DEFAULT 0"),
        new("MemberTargets", "\"Sex\" INTEGER NOT NULL DEFAULT 0"),
        new("MemberTargets", "\"AgeYears\" INTEGER NOT NULL DEFAULT 30"),
        new("MemberTargets", "\"HeightCm\" REAL NOT NULL DEFAULT 170"),
        new("MemberTargets", "\"WeightKg\" REAL NOT NULL DEFAULT 70"),
        new("MemberTargets", "\"Activity\" REAL NOT NULL DEFAULT 1.375"),
        new("MemberTargets", "\"Goal\" INTEGER NOT NULL DEFAULT 1"),
        new("Dishes", "\"CookMinutes\" INTEGER NOT NULL DEFAULT 30"),
        new("Dishes", "\"Difficulty\" INTEGER NOT NULL DEFAULT 2"),
        new("Dishes", "\"Instructions\" TEXT NOT NULL DEFAULT ''")
    };

    public static void Run(AppDbContext db)
    {
        db.Database.EnsureCreated();
        UpgradeSchema(db);
        SeedMissingFoods(db);
    }

    private static void UpgradeSchema(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS ""DiaryEntries"" (
            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_DiaryEntries"" PRIMARY KEY AUTOINCREMENT,
            ""MemberId"" INTEGER NOT NULL,
            ""Date"" TEXT NOT NULL,
            ""Label"" TEXT NOT NULL,
            ""Kcal"" REAL NOT NULL,
            ""Protein"" REAL NOT NULL,
            ""Fat"" REAL NOT NULL,
            ""Carbs"" REAL NOT NULL,
            ""CreatedUtc"" TEXT NOT NULL)");
        db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_DiaryEntries_MemberId_Date"" ON ""DiaryEntries"" (""MemberId"", ""Date"")");
        db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS ""MemberTargets"" (
            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_MemberTargets"" PRIMARY KEY AUTOINCREMENT,
            ""MemberId"" INTEGER NOT NULL,
            ""Kcal"" REAL NOT NULL)");

        foreach (var column in AddedColumns) AddColumnIfMissing(db, column);

        db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_MemberTargets_MemberId"" ON ""MemberTargets"" (""MemberId"")");
    }

    private static void AddColumnIfMissing(AppDbContext db, ColumnSpec column)
    {
        try
        {
            db.Database.ExecuteSqlRaw($"ALTER TABLE \"{column.Table}\" ADD COLUMN {column.Definition}");
        }
        catch (Exception ex) when (ex.ToString().Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private static void SeedMissingFoods(AppDbContext db)
    {
        var existing = db.Foods.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = FoodCatalog.LoadFull().Where(f => !existing.Contains(f.Name)).ToList();
        if (missing.Count == 0) return;

        foreach (var food in missing) food.Id = 0;
        db.Foods.AddRange(missing);
        db.SaveChanges();
    }
}
