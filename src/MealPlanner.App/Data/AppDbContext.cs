using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.App.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Food> Foods => Set<Food>();
    public DbSet<Household> Households => Set<Household>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<HouseholdDish> Dishes => Set<HouseholdDish>();
    public DbSet<HouseholdDishIngredient> DishIngredients => Set<HouseholdDishIngredient>();
    public DbSet<PantryEntry> Pantry => Set<PantryEntry>();
    public DbSet<DiaryEntry> DiaryEntries => Set<DiaryEntry>();
    public DbSet<MemberTarget> MemberTargets => Set<MemberTarget>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Food>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.Name).IsRequired().HasMaxLength(200);
            e.Property(f => f.Category).HasMaxLength(100);
            e.HasIndex(f => f.Name);
        });

        b.Entity<Household>(e => e.HasIndex(h => h.Code).IsUnique());

        b.Entity<Member>(e =>
        {
            e.HasIndex(m => m.TelegramUserId).IsUnique();
            e.HasOne(m => m.Household).WithMany().HasForeignKey(m => m.HouseholdId);
        });

        b.Entity<HouseholdDish>(e =>
        {
            e.Property(d => d.Name).IsRequired().HasMaxLength(200);
            e.HasMany(d => d.Ingredients).WithOne(i => i.Dish).HasForeignKey(i => i.DishId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => d.HouseholdId);
        });

        b.Entity<HouseholdDishIngredient>(e =>
            e.HasOne(i => i.Food).WithMany().HasForeignKey(i => i.FoodId).OnDelete(DeleteBehavior.Restrict));

        b.Entity<PantryEntry>(e =>
        {
            e.HasIndex(p => new { p.HouseholdId, p.FoodId }).IsUnique();
            e.HasOne(p => p.Food).WithMany().HasForeignKey(p => p.FoodId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DiaryEntry>(e => e.HasIndex(d => new { d.MemberId, d.Date }));
        b.Entity<MemberTarget>(e => e.HasIndex(t => t.MemberId).IsUnique());
    }

    public static void Initialize(AppDbContext db)
    {
        db.Database.EnsureCreated();
        UpgradeSchema(db);

        var existing = db.Foods.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = FoodCatalog.LoadFull().Where(f => !existing.Contains(f.Name)).ToList();
        if (missing.Count == 0) return;

        foreach (var food in missing) food.Id = 0;
        db.Foods.AddRange(missing);
        db.SaveChanges();
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
            ""Kcal"" REAL NOT NULL,
            ""HasProfile"" INTEGER NOT NULL DEFAULT 0,
            ""IsManual"" INTEGER NOT NULL DEFAULT 0,
            ""Sex"" INTEGER NOT NULL DEFAULT 0,
            ""AgeYears"" INTEGER NOT NULL DEFAULT 30,
            ""HeightCm"" REAL NOT NULL DEFAULT 170,
            ""WeightKg"" REAL NOT NULL DEFAULT 70,
            ""Activity"" REAL NOT NULL DEFAULT 1.375,
            ""Goal"" INTEGER NOT NULL DEFAULT 1)");
        foreach (var col in new[]
        {
            "\"HasProfile\" INTEGER NOT NULL DEFAULT 0", "\"IsManual\" INTEGER NOT NULL DEFAULT 0",
            "\"Sex\" INTEGER NOT NULL DEFAULT 0", "\"AgeYears\" INTEGER NOT NULL DEFAULT 30",
            "\"HeightCm\" REAL NOT NULL DEFAULT 170", "\"WeightKg\" REAL NOT NULL DEFAULT 70",
            "\"Activity\" REAL NOT NULL DEFAULT 1.375", "\"Goal\" INTEGER NOT NULL DEFAULT 1"
        })
        {
            try { db.Database.ExecuteSqlRaw($"ALTER TABLE \"MemberTargets\" ADD COLUMN {col}"); }
            catch (Exception ex) when (ex.ToString().Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
        }
        foreach (var col in new[] { "\"CookMinutes\" INTEGER NOT NULL DEFAULT 30", "\"Difficulty\" INTEGER NOT NULL DEFAULT 2" })
        {
            try { db.Database.ExecuteSqlRaw($"ALTER TABLE \"Dishes\" ADD COLUMN {col}"); }
            catch (Exception ex) when (ex.ToString().Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
        }
        db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_MemberTargets_MemberId"" ON ""MemberTargets"" (""MemberId"")");
    }
}
