using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// История треда для модели (спец. assistant, 12.4).
///
/// В модель едут резюме плюс последние N сообщений — не весь тред: у длинного хода
/// за один прогон контекст растёт быстрее, чем у человека за неделю переписки.
/// В ленте при этом остаётся всё: человек видит свою переписку целиком.
/// </summary>
public class HistoryBuilder(ApplicationContext context, IOptions<AssistantOptions> options)
{
    public async Task<AssistantHistory> Build(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        var settings = options.Value;

        var thread = context.AssistantMessages
            .Where(x => x.UserId == userId && x.WorkspaceId == workspaceId);

        // Резюме компакции отрезает всё, что было до него: свёрнутая часть разговора
        // едет в модель им одним
        var summary = await thread
            .Where(x => x.Role == AssistantMessageRole.Summary)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var after = summary == null
            ? thread
            : thread.Where(x => x.CreatedAtUtc > summary.CreatedAtUtc);

        var recent = await after
            .Where(x => x.Role != AssistantMessageRole.Summary)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(settings.HistoryMessages)
            .Select(x => new { x.Id, x.Role, x.Content })
            .ToArrayAsync(ct);

        var ordered = recent.OrderBy(x => x.Id).ToList();

        // Потолок по символам — вторая страховка на случай, когда компакция ещё
        // не срабатывала, а человек уже наговорил на весь контекст. Режем с начала:
        // ближе к концу разговор нужнее
        var total = ordered.Sum(x => x.Content.Length);
        while (ordered.Count > 1 && total > settings.CompactionThreshold)
        {
            total -= ordered[0].Content.Length;
            ordered.RemoveAt(0);
        }

        var messages = ordered
            .Select(x => new ChatMessage(
                x.Role == AssistantMessageRole.User ? ChatRole.User : ChatRole.Assistant,
                x.Content))
            .ToList();

        // Незакрытый план прикладывается всегда и отдельно от истории: на то, что
        // модель пересказала его в прозе, полагаться нельзя (12.4)
        var pending = await context.AssistantPlans
            .Where(x => x.UserId == userId
                        && x.WorkspaceId == workspaceId
                        && (x.Status == AssistantPlanStatus.Pending || x.Status == AssistantPlanStatus.Draft))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        return new AssistantHistory
        {
            Messages = messages,
            Summary = summary?.Content,
            PendingPlan = pending
        };
    }
}
