using MealPlanner.App.Data;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.App.Services;

public record JoinResult(bool Found, string Code, int MemberCount);

public interface IHouseholdService
{
    Task<Member> EnsureMemberAsync(long telegramUserId, string name, CancellationToken ct);

    Task<JoinResult> JoinAsync(long telegramUserId, string name, string code, CancellationToken ct);

    Task<int> CountMembersAsync(int householdId, CancellationToken ct);
}

/// <summary>
/// Members and households: creation and joining by code
/// </summary>
public class HouseholdService : IHouseholdService
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;

    private readonly AppDbContext _db;

    public HouseholdService(AppDbContext db) => _db = db;

    public async Task<Member> EnsureMemberAsync(long telegramUserId, string name, CancellationToken ct)
    {
        var member = await _db.Members.Include(m => m.Household)
            .FirstOrDefaultAsync(m => m.TelegramUserId == telegramUserId, ct);
        if (member != null) return member;

        member = new Member
        {
            TelegramUserId = telegramUserId,
            Name = name,
            Household = new Household { Code = await NewCodeAsync(ct) }
        };
        _db.Members.Add(member);
        await _db.SaveChangesAsync(ct);
        return member;
    }

    public async Task<JoinResult> JoinAsync(long telegramUserId, string name, string code, CancellationToken ct)
    {
        code = code.Trim().ToUpperInvariant();
        var household = await _db.Households.FirstOrDefaultAsync(h => h.Code == code, ct);
        if (household is null) return new JoinResult(false, code, 0);

        var member = await _db.Members.FirstOrDefaultAsync(m => m.TelegramUserId == telegramUserId, ct);
        if (member is null)
            _db.Members.Add(new Member { TelegramUserId = telegramUserId, Name = name, HouseholdId = household.Id });
        else
            member.HouseholdId = household.Id;
        await _db.SaveChangesAsync(ct);

        return new JoinResult(true, household.Code, await CountMembersAsync(household.Id, ct));
    }

    public Task<int> CountMembersAsync(int householdId, CancellationToken ct) =>
        _db.Members.CountAsync(m => m.HouseholdId == householdId, ct);

    private async Task<string> NewCodeAsync(CancellationToken ct)
    {
        while (true)
        {
            string code = new(Enumerable.Range(0, CodeLength).Select(_ => CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)]).ToArray());
            if (!await _db.Households.AnyAsync(h => h.Code == code, ct)) return code;
        }
    }
}
