using System.Collections.Concurrent;
using MealPlanner.Core.Models;

namespace MealPlanner.App.Bot;

public enum Step { None, AddName, AddCustomGrams, NewFoodValues, IdeaText, JoinCode, SearchFood, DishSearch, OwnSearch,
    EditGrams, EditAddGrams, LogGrams, GoalText,
    ProfAge, ProfHeight, ProfWeight }

/// <summary>
/// Composition screen mode: recipe editing or cooking with actual grams
/// </summary>
public enum CookMode { Edit, Cook }

/// <summary>
/// Profile draft during the norm calculation wizard; empty fields are replaced with default values for the estimate
/// </summary>
public class ProfileDraft
{
    public Sex? Sex { get; set; }
    public int? Age { get; set; }
    public double? Height { get; set; }
    public double? Weight { get; set; }
    public double? Activity { get; set; }
    public DietGoal? Goal { get; set; }

    public void Clear() { Sex = null; Age = null; Height = null; Weight = null; Activity = null; Goal = null; }
}

public record ResultItem(Dish Dish, bool IsTemplate, int? DbId);

public record FitItem(Dish Dish, double Portions, bool IsTemplate);

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
    public int DraftServings { get; set; } = 1;
    public int DraftMinutes { get; set; } = 30;

    public HashSet<int> Selected { get; } = new();
    public List<ResultItem> Results { get; } = new();
    public bool QuickOnly { get; set; }
    public bool IdeaBuy { get; set; }
    public char SearchCtx { get; set; } = 'a';
    public string SearchQuery { get; set; } = string.Empty;
    public string CatBack { get; set; } = "b:home";
    public string CatQuery { get; set; } = string.Empty;
    public string OwnQuery { get; set; } = string.Empty;
    public int MaxDifficulty { get; set; } = 3;

    public List<FitItem> Fit { get; } = new();

    public CookMode Mode { get; set; }
    public int EditDishId { get; set; }
    public string EditName { get; set; } = string.Empty;
    public int EditServings { get; set; } = 1;
    public int EditIndex { get; set; }
    public int EditMinutes { get; set; } = 30;
    public int EditDifficulty { get; set; } = 2;
    public int EditPendingFoodId { get; set; }
    public List<(int FoodId, double Grams)> Items { get; } = new();
    public Dictionary<int, double> Recipe { get; } = new();

    public ProfileDraft Prof { get; } = new();

    public int DiaryOffset { get; set; }
    public int LogFoodId { get; set; }

    public void ResetDraft()
    {
        DraftName = string.Empty;
        Draft.Clear();
        PendingFoodId = 0;
        NewFoodName = string.Empty;
        DraftServings = 1;
        DraftMinutes = 30;
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
