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
    }

    public static void Initialize(AppDbContext db)
    {
        db.Database.EnsureCreated();
        if (db.Foods.Any()) return;

        db.Foods.AddRange(FoodCatalog.LoadSeed());
        db.SaveChanges();
    }
}
