using System.Collections.Concurrent;
using MealPlanner.Core.Models;

namespace MealPlanner.App.Bot;

public enum Step { None, AddName, AddCustomGrams, AddSearch, NewFoodValues, IdeaText, JoinCode,
    EditGrams, EditAddGrams, LogGrams, GoalText }

/// <summary>
/// Composition screen mode: recipe editing or cooking with actual grams
/// </summary>
public enum CookMode { Edit, Cook }

public record ResultItem(Dish Dish, bool IsTemplate, int? DbId);

/// <summary>
/// Short-lived conversation with the user: selections made in the cook and add dish wizards
/// </summary>
public class Session
{
    public Step Step { get; set; } = Step.None;

    public string DraftName { get; set; } = string.Empty;
    public List<(int FoodId, double Grams)> Draft { get; } = new();
    public int PendingFoodId { get; set; }
    public string NewFoodName { get; set; } = string.Empty;

    public HashSet<int> Selected { get; } = new();
    public List<ResultItem> Results { get; } = new();

    public CookMode Mode { get; set; }
    public int EditDishId { get; set; }
    public string EditName { get; set; } = string.Empty;
    public int EditServings { get; set; } = 1;
    public int EditIndex { get; set; }
    public int EditPendingFoodId { get; set; }
    public List<(int FoodId, double Grams)> Items { get; } = new();
    public Dictionary<int, double> Recipe { get; } = new();

    public int DiaryOffset { get; set; }
    public int LogFoodId { get; set; }

    public void ResetDraft()
    {
        DraftName = string.Empty;
        Draft.Clear();
        PendingFoodId = 0;
        NewFoodName = string.Empty;
    }
}

/// <summary>
/// In-memory session store, enough for a bot with two or three users
/// </summary>
public class SessionStore
{
    private readonly ConcurrentDictionary<long, Session> _sessions = new();

    public Session Get(long userId) => _sessions.GetOrAdd(userId, _ => new Session());
}
