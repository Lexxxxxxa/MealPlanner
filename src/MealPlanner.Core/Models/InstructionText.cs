using System.Text.RegularExpressions;

namespace MealPlanner.Core.Models;

/// <summary>
/// Normalization of a recipe entered by the user: one step per line, without numbering and markers
/// </summary>
public static class InstructionText
{
    public const int MaxLength = 3000;

    private static readonly Regex Marker = new(@"^\s*(?:\d+\s*[.)]|[-•*–—])\s*", RegexOptions.Compiled);

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var steps = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Marker.Replace(line, string.Empty).Trim())
            .Where(line => line.Length > 0);
        string joined = string.Join('\n', steps);
        return joined.Length <= MaxLength ? joined : joined[..MaxLength];
    }

    public static IReadOnlyList<string> Split(string? instructions) =>
        (instructions ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Format(IReadOnlyList<string> steps) =>
        string.Join('\n', steps.Select((step, i) => $"{i + 1}. {step}"));
}
