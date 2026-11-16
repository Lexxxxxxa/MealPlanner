using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MealPlanner.App.Data;
using MealPlanner.App.Services;
using MealPlanner.Core.Data;
using MealPlanner.Core.Models;
using MealPlanner.Core.Parsing;
using MealPlanner.Core.Recommendation;
using static MealPlanner.App.Bot.UiKit;

namespace MealPlanner.App.Bot;

/// <summary>
/// Saved dishes of the household: list, search, dish card, deletion
/// </summary>
internal sealed class DishesFeature : FeatureBase
{
    public DishesFeature(BotContext ctx) : base(ctx)
    {
    }

    public override IReadOnlyCollection<string> Prefixes { get; } = new[] { "d" };

    public override IReadOnlyCollection<Step> Steps { get; } = new[] { Step.OwnSearch, Step.EditInstructions };

    public override Task<BotReply> HandleCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct) =>
        DishCallbackAsync(userId, userName, s, p, ct);

    public override async Task<BotReply> HandleStepAsync(long userId, string userName, Session s, string text, CancellationToken ct)
    {
        if (s.Step == Step.EditInstructions) return await SaveInstructionsAsync(userId, userName, s, InstructionText.Normalize(text), ct);

        s.Step = Step.None;
        s.OwnQuery = text;
        return await DishListAsync(userId, userName, ct);
    }

    internal Task<BotReply> OpenAsync(long userId, string userName, Session s, CancellationToken ct)
    {
        s.Step = Step.None;
        s.OwnQuery = string.Empty;
        return DishListAsync(userId, userName, ct);
    }

    private async Task<BotReply> SaveInstructionsAsync(long userId, string userName, Session s, string instructions, CancellationToken ct)
    {
        s.Step = Step.None;
        var member = await MemberAsync(userId, userName, ct);
        var dishes = await Services.Dishes.ListAsync(member.HouseholdId, await FoodsAsync(ct), ct);
        var found = dishes.FirstOrDefault(d => d.Entity.Id == s.EditDishId);
        if (found is null) return await DishListAsync(userId, userName, ct);

        await Services.Dishes.UpdateAsync(member.HouseholdId, found.Entity.Id, found.Model with { Instructions = instructions }, ct);
        string note = instructions.Length == 0 ? "🗑 Рецепт удалён." : "✅ Рецепт сохранён.";
        return new BotReply(note, new[] { Row(new InlineButton("📖 К блюду", $"d:v:{found.Entity.Id}")) });
    }

    internal async Task<BotReply> DishCallbackAsync(long userId, string userName, Session s, string[] p, CancellationToken ct)
    {
        string action = p.Length > 1 ? p[1] : "";
        if (action == "list") return await DishListAsync(userId, userName, ct, PageArg(p, 2));
        if (action == "find")
        {
            s.Step = Step.OwnSearch;
            return new BotReply("Напишите название блюда или продукт, например: «борщ» или «курица»");
        }
        if (action == "clr")
        {
            s.OwnQuery = string.Empty;
            return await DishListAsync(userId, userName, ct);
        }

        var member = await MemberAsync(userId, userName, ct);
        int id = p.Length > 2 ? int.Parse(p[2], CultureInfo.InvariantCulture) : 0;
        var foods = await FoodsAsync(ct);
        var dishes = await Services.Dishes.ListAsync(member.HouseholdId, foods, ct);
        var found = dishes.FirstOrDefault(d => d.Entity.Id == id);
        if (found is null) return await DishListAsync(userId, userName, ct);
        var dish = found.Model;

        switch (action)
        {
            case "v":
            {
                var sb = new StringBuilder($"📖 {dish.Name} ({dish.Servings} порц.)\n{TimeInfo(dish)}\n");
                sb.AppendLine("На порцию: " + Nutr(dish.PerServing));
                sb.AppendLine("Всего: " + Nutr(dish.Total));
                sb.AppendLine("\nСостав:");
                foreach (var ing in dish.Ingredients) sb.AppendLine($"• {ing.Food.Name} — {ing.Grams:F0} г");
                sb.Append(RecipeText(dish));
                return new BotReply(sb.ToString().TrimEnd(), new[]
                {
                    Row(new InlineButton("👩‍🍳 Готовлю", $"d:cook:{id}"), new InlineButton("🍽 Съел(а)", $"d:eat:{id}")),
                    Row(new InlineButton("✏️ Изменить", $"d:edit:{id}"), new InlineButton(dish.Steps.Count > 0 ? "📝 Изменить рецепт" : "📝 Добавить рецепт", $"d:rec:{id}")),
                    Row(new InlineButton("🗑 Удалить", $"d:del:{id}")),
                    Row(new InlineButton("⬅️ К списку", "d:list"))
                });
            }
            case "rec":
            {
                s.EditDishId = id;
                s.Step = Step.EditInstructions;
                string current = dish.Steps.Count > 0 ? "\n\nСейчас:\n" + InstructionText.Format(dish.Steps) : string.Empty;
                var buttons = new List<InlineButton> { new("⬅️ Отмена", $"d:v:{id}") };
                if (dish.Steps.Count > 0) buttons.Insert(0, new InlineButton("🗑 Удалить рецепт", $"d:rdel:{id}"));
                return new BotReply($"📝 Напишите шаги рецепта «{dish.Name}»: каждый шаг с новой строки (нумерация не нужна).{current}", new[] { buttons });
            }
            case "rdel":
                s.EditDishId = id;
                return await SaveInstructionsAsync(userId, userName, s, string.Empty, ct);
            case "edit":
            case "cook":
                EditFeature.StartEditing(s, found.Entity, dish, action == "cook" ? CookMode.Cook : CookMode.Edit);
                return EditFeature.EditScreen(s, foods, null);
            case "eat":
            {
                var portions = new[] { 0.5, 1, 1.5, 2, 3 };
                return new BotReply($"Сколько порций «{dish.Name}» вы съели?\nНа порцию: {Nutr(dish.PerServing)}",
                    Rows(portions.Select(x => new InlineButton(PortionText(x), $"d:ate:{id}:{x.ToString(CultureInfo.InvariantCulture)}")), 3)
                        .Append(Row(new InlineButton("⬅️ Назад", $"d:v:{id}"))).ToList());
            }
            case "ate":
            {
                double portions = double.Parse(p[3], CultureInfo.InvariantCulture);
                await Services.Diary.AddAsync(member.Id, Ctx.Today(), dish.Name + " × " + PortionText(portions), dish.PerServing * portions, ct);
                s.DiaryOffset = 0;
                return await Ctx.Features.Diary.DayAsync(userId, userName, s, ct);
            }
            case "del":
                return new BotReply($"Удалить «{dish.Name}»?", new[]
                {
                    Row(new InlineButton("Да, удалить", $"d:ok:{id}"), new InlineButton("Отмена", $"d:v:{id}"))
                });
            case "ok":
            {
                await Services.Dishes.DeleteAsync(member.HouseholdId, id, ct);
                return await DishListAsync(userId, userName, ct);
            }
            default: return await DishListAsync(userId, userName, ct);
        }
    }

    internal async Task<BotReply> DishListAsync(long userId, string userName, CancellationToken ct, int page = 0)
    {
        var s = Ctx.Sessions.Get(userId);
        var member = await MemberAsync(userId, userName, ct);
        var foods = await FoodsAsync(ct);
        var dishes = await Services.Dishes.ListAsync(member.HouseholdId, foods, ct);

        if (dishes.Count == 0)
            return new BotReply("📖 В вашей базе пока нет блюд.\nНажмите «➕ Добавить блюдо» внизу, сохраните блюдо из «📚 База блюд» или посмотрите идеи в «🍳 Что приготовить».");

        var shown = dishes.Where(d => DishMatches(s.OwnQuery, d.Model)).ToList();
        int pages = Math.Max(1, (shown.Count + DishPageSize - 1) / DishPageSize);
        page = Math.Clamp(page, 0, pages - 1);

        var rows = Rows(shown.Skip(page * DishPageSize).Take(DishPageSize).Select(d => new InlineButton(
            $"{d.Model.Name} · {d.Model.PerServing.Kcal:F0} ккал", $"d:v:{d.Entity.Id}")), 1);
        AddNav(rows, page, pages, pg => $"d:list:{pg}");
        var footer = new List<InlineButton> { new("🔎 Найти", "d:find") };
        if (s.OwnQuery.Length > 0) footer.Add(new InlineButton("✖️ Сбросить поиск", "d:clr"));
        rows.Add(footer);

        string head = s.OwnQuery.Length > 0
            ? $"📖 «{s.OwnQuery.Trim()}» — найдено {shown.Count} из {dishes.Count}"
            : $"📖 Ваши блюда ({dishes.Count}):\nНажмите на блюдо, чтобы увидеть состав.";
        if (shown.Count == 0) head += "\nНичего не нашла, попробуйте другое слово.";
        return new BotReply(pages > 1 ? $"{head}\nСтраница {page + 1} из {pages}" : head, rows);
    }
}
