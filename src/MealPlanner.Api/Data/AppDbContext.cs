using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Food> Foods => Set<Food>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Food>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.Name).IsRequired().HasMaxLength(200);
            e.Property(f => f.Category).HasMaxLength(100);
            e.HasIndex(f => f.Name);
        });

        modelBuilder.Entity<Recipe>(e =>
        {
            e.Property(r => r.Name).IsRequired().HasMaxLength(200);
            e.HasMany(r => r.Ingredients)
                .WithOne(i => i.Recipe)
                .HasForeignKey(i => i.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecipeIngredient>(e =>
        {
            e.HasOne(i => i.Food)
                .WithMany()
                .HasForeignKey(i => i.FoodId)
                .OnDelete(DeleteBehavior.Restrict);
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
