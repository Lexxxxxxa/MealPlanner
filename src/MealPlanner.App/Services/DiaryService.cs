using MealPlanner.App.Data;
using MealPlanner.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.App.Services;

/// <summary>
/// Day summary: what was eaten, the norm and the remainder in calories and macros
/// </summary>
public record DaySummary(
    IReadOnlyList<DiaryEntry> Entries, Nutrients Eaten, Nutrients Goal, MemberTarget? Profile)
{
    public Nutrients Remaining => new(
        Goal.Kcal - Eaten.Kcal, Goal.Protein - Eaten.Protein, Goal.Fat - Eaten.Fat, Goal.Carbs - Eaten.Carbs);

    public double TargetKcal => Goal.Kcal;
}

public interface IDiaryService
{
    Task AddAsync(int memberId, DateOnly date, string label, Nutrients nutrients, CancellationToken ct);

    Task DeleteAsync(int memberId, int entryId, CancellationToken ct);

    Task<DaySummary> SummaryAsync(int memberId, DateOnly date, CancellationToken ct);

    Task<MemberTarget?> GetTargetAsync(int memberId, CancellationToken ct);

    Task SetManualTargetAsync(int memberId, double kcal, CancellationToken ct);

    Task<double> SaveProfileAsync(int memberId, Sex sex, int age, double heightCm, double weightKg,
        double activity, DietGoal goal, CancellationToken ct);
}

/// <summary>
/// Food diary, daily norm and member profile
/// </summary>
public class DiaryService : IDiaryService
{
    public const double DefaultTargetKcal = 2000;
    private static readonly (double P, double F, double C) DefaultShares = (0.20, 0.30, 0.50);

    private readonly AppDbContext _db;

    public DiaryService(AppDbContext db) => _db = db;

    public async Task AddAsync(int memberId, DateOnly date, string label, Nutrients n, CancellationToken ct)
    {
        _db.DiaryEntries.Add(new DiaryEntry
        {
            MemberId = memberId, Date = date, Label = label,
            Kcal = n.Kcal, Protein = n.Protein, Fat = n.Fat, Carbs = n.Carbs
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int memberId, int entryId, CancellationToken ct)
    {
        var entry = await _db.DiaryEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.MemberId == memberId, ct);
        if (entry is null) return;
        _db.DiaryEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<DaySummary> SummaryAsync(int memberId, DateOnly date, CancellationToken ct)
    {
        var entries = await _db.DiaryEntries.AsNoTracking()
            .Where(e => e.MemberId == memberId && e.Date == date)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);
        var profile = await GetTargetAsync(memberId, ct);

        double target = profile?.Kcal ?? DefaultTargetKcal;
        var (p, f, c) = profile is { HasProfile: true, IsManual: false } ? EnergyCalculator.MacroShares(profile.Goal) : DefaultShares;

        var eaten = new Nutrients(entries.Sum(e => e.Kcal), entries.Sum(e => e.Protein), entries.Sum(e => e.Fat), entries.Sum(e => e.Carbs));
        var goal = new Nutrients(target, target * p / 4, target * f / 9, target * c / 4);
        return new DaySummary(entries, eaten, goal, profile);
    }

    public Task<MemberTarget?> GetTargetAsync(int memberId, CancellationToken ct) =>
        _db.MemberTargets.AsNoTracking().FirstOrDefaultAsync(t => t.MemberId == memberId, ct);

    public async Task SetManualTargetAsync(int memberId, double kcal, CancellationToken ct)
    {
        var target = await TrackedTargetAsync(memberId, ct);
        target.Kcal = kcal;
        target.IsManual = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<double> SaveProfileAsync(int memberId, Sex sex, int age, double heightCm, double weightKg,
        double activity, DietGoal goal, CancellationToken ct)
    {
        double kcal = EnergyCalculator.DailyTarget(sex, age, heightCm, weightKg, activity, goal);
        var target = await TrackedTargetAsync(memberId, ct);
        target.Sex = sex;
        target.AgeYears = age;
        target.HeightCm = heightCm;
        target.WeightKg = weightKg;
        target.Activity = activity;
        target.Goal = goal;
        target.HasProfile = true;
        target.IsManual = false;
        target.Kcal = kcal;
        await _db.SaveChangesAsync(ct);
        return kcal;
    }

    private async Task<MemberTarget> TrackedTargetAsync(int memberId, CancellationToken ct)
    {
        var target = await _db.MemberTargets.FirstOrDefaultAsync(t => t.MemberId == memberId, ct);
        if (target is null)
        {
            target = new MemberTarget { MemberId = memberId };
            _db.MemberTargets.Add(target);
        }
        return target;
    }
}
