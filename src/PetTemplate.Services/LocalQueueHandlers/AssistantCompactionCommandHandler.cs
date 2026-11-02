using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Assistant.Models;
using PetTemplate.Services.Billing;
using PetTemplate.Services.Commands;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.LocalQueueHandlers;

/// <summary>
/// Компакция длинного контекста (спец. assistant, 12.4).
///
/// Обрезка истории с начала не годится: она молча теряет нить, а у длинного хода
/// за один прогон контекст растёт быстрее, чем у человека за неделю переписки.
/// Поэтому старая часть треда сворачивается отдельным вызовом модели в резюме,
/// а исходные сообщения остаются в базе и в ленте — человек видит свою переписку
/// целиком, и только в модель едут резюме плюс последние N.
///
/// Резюме — служебный расход, а не ход человека (спец. assistant, 13.5): строка
/// <see cref="ServiceSpend"/> по модели, которая ответила. В лимит пространства она
/// не идёт, а в счёт поставщика — идёт.
/// </summary>
public class AssistantCompactionCommandHandler(
    ApplicationContext context,
    IModelChatClients clients,
    AiModelCatalog models,
    ModelBreaker breaker,
    BillingMetrics billing,
    IOptions<AssistantOptions> options,
    ILogger<AssistantCompactionCommandHandler> logger) : ICommandHandler<AssistantCompactionCommand>
{
    private static readonly Lazy<string> Prompt = new(() => Prompts.Read("compaction.md"));

    public async Task Handle(AssistantCompactionCommand command, CancellationToken ct)
    {
        var keep = options.Value.HistoryMessages;

        var thread = context.AssistantMessages
            .Where(x => x.UserId == command.UserId && x.WorkspaceId == command.WorkspaceId);

        var summary = await thread
            .Where(x => x.Role == AssistantMessageRole.Summary)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var after = summary == null
            ? thread
            : thread.Where(x => x.CreatedAtUtc > summary.CreatedAtUtc);

        var messages = await after
            .Where(x => x.Role != AssistantMessageRole.Summary)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .ToArrayAsync(ct);

        // Сворачивать нечего: команда повторилась, а тред уже сжат. Сжимать последние N
        // нельзя — именно они едут в модель вместе с резюме
        var compact = messages.Length > keep ? messages[..^keep] : [];
        if (compact.Length == 0) return;

        var text = new StringBuilder(Prompt.Value);
        text.AppendLine();
        foreach (var message in compact)
            text.AppendLine($"{(message.Role == AssistantMessageRole.User ? "Человек" : "Ассистент")}: {message.Content}");

        // Резюме пишет та же модель, что ходит: отдельной «дешёвой для сжатия»
        // в справочнике нет. Ходить некуда — тред подождёт следующей команды
        var pick = await models.Pick(ct);
        if (pick == null) return;

        // Это такое же обращение к модели, как ход, и размыкатель о нём знает:
        // выбор мог взять пробу после паузы, и без отчёта модель провисела бы
        // разомкнутой ещё одну паузу (16.2)
        ChatResponse response;
        try
        {
            response = await clients.For(pick.Model).GetResponseAsync(
                [new ChatMessage(ChatRole.User, text.ToString())], new ChatOptions(), ct);
        }
        catch (Exception e)
        {
            await breaker.Failed(pick, e, ct);
            throw;
        }

        await breaker.Succeeded(pick);

        // Вызов был и стоил денег, даже если резюме вышло пустым. Без usage строка всё
        // равно пишется, с нулями: её пропажа выглядела бы как «компакций не было»
        if (response.Usage == null)
            logger.LogWarning("Model {ModelKey} returned no usage for compaction", pick.Model.Key);
        var spend = ServiceSpending.Of(
            ServiceSpendKind.Compaction, pick.Model, response.Usage, command.WorkspaceId);
        context.ServiceSpend.Add(spend);
        billing.Cost(pick.Model.Key, BillingMetrics.Kind(ServiceSpendKind.Compaction), spend.Cost);

        var content = response.Text.Trim();
        if (content.Length == 0)
        {
            await context.SaveChangesAsync(ct);
            return;
        }

        // Резюме встаёт ровно в точку разреза, а не в конец ленты: после него идут
        // те самые последние N, которые в него не вошли, — и именно их вместе
        // с резюме собирает HistoryBuilder. Микросекунда — шаг timestamptz
        var at = DateTime.SpecifyKind(compact[^1].CreatedAtUtc, DateTimeKind.Utc).AddTicks(10);

        await context.AssistantMessages.AddAsync(new AssistantMessage
        {
            Id = Guid.CreateVersion7(new DateTimeOffset(at)),
            UserId = command.UserId,
            WorkspaceId = command.WorkspaceId,
            Role = AssistantMessageRole.Summary,
            Content = content,
            CreatedAtUtc = at
        }, ct);

        await context.SaveChangesAsync(ct);
    }
}
