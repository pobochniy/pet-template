using PetTemplate.Services.Admin;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Constants;

namespace PetTemplate.Api.Workers;

/// <summary>
/// Суточная уборка:
///
/// <list type="bullet">
/// <item>отработавшие приглашения (спец. authorization, 6.2): через
/// <see cref="InvitationConstants.Retention"/> после выдачи строка уходит из базы
/// вместе с дочерними правами;</item>
/// <item>приглашения админов (спец. admin, 3.4) — по тому же правилу, в любом статусе;</item>
/// <item>заброшенные пространства (спец. authorization, 8): без единого участника
/// и без действий внутри дольше <see cref="WorkspaceConstants.AbandonedRetention"/>;</item>
/// <item>шаги трассы ассистента старше <c>TraceRetentionDays</c> (спец. assistant, 13.1):
/// сам прогон остаётся;</item>
/// <item>журнал доступа к диалогам старше <c>AccessLogRetentionDays</c> (спец. admin, 16.2) —
/// живёт дольше трассы, которую описывает;</item>
/// <item>автооценки диалогов старше <c>EvaluationRetentionDays</c> (спец. admin, 16.3).</item>
/// </list>
///
/// Расписание у них одно, поэтому и воркер один, а не свой на каждую чистку:
/// другие суточные чистки дописываются сюда же. Идемпотентна: повторный проход, в том числе на втором экземпляре API, просто
/// ничего не находит. Порядок важен — приглашения уходят первыми,
/// чтобы удаление пространства не спотыкалось о свои же строки.
/// </summary>
public class DailyCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DailyCleanupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period);

        try
        {
            // Первый проход сразу после старта, а не через сутки: иначе приложение,
            // которое перезапускают чаще раза в день, не почистит базу никогда
            do
            {
                await Cleanup(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка хоста — не ошибка
        }
    }

    private async Task Cleanup(CancellationToken ct)
    {
        try
        {
            // Своя область на проход: <c>DbContext</c> живёт запрос, а воркер —
            // всё время работы приложения, и держать scoped-сервис в поле нельзя
            await using var scope = scopeFactory.CreateAsyncScope();
            var invitations = scope.ServiceProvider.GetRequiredService<WorkspaceInvitationService>();
            var workspaces = scope.ServiceProvider.GetRequiredService<WorkspaceService>();
            var adminInvitations = scope.ServiceProvider.GetRequiredService<AdminInvitationService>();
            var traces = scope.ServiceProvider.GetRequiredService<TraceWriter>();
            var accessLog = scope.ServiceProvider.GetRequiredService<AdminAccessLogService>();
            var evaluations = scope.ServiceProvider.GetRequiredService<DialogEvaluationService>();

            var deletedInvitations = await invitations.CleanupOld(ct);

            if (deletedInvitations > 0)
            {
                logger.LogInformation("Invitation cleanup: {Count} rows deleted", deletedInvitations);
            }

            var deletedAdminInvitations = await adminInvitations.CleanupOld(ct);

            if (deletedAdminInvitations > 0)
            {
                logger.LogInformation("Admin invitation cleanup: {Count} rows deleted", deletedAdminInvitations);
            }

            var deletedWorkspaces = await workspaces.CleanupAbandoned(ct);

            if (deletedWorkspaces > 0)
            {
                logger.LogInformation("Workspace cleanup: {Count} abandoned workspaces deleted", deletedWorkspaces);
            }

            var deletedTraceSteps = await traces.CleanupOld(ct);

            if (deletedTraceSteps > 0)
            {
                logger.LogInformation("Assistant trace cleanup: {Count} steps deleted", deletedTraceSteps);
            }

            var deletedAccessLog = await accessLog.CleanupOld(ct);

            if (deletedAccessLog > 0)
            {
                logger.LogInformation("Dialog access log cleanup: {Count} rows deleted", deletedAccessLog);
            }

            var deletedEvaluations = await evaluations.CleanupOld(ct);

            if (deletedEvaluations > 0)
            {
                logger.LogInformation("Dialog evaluation cleanup: {Count} rows deleted", deletedEvaluations);
            }
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // Необработанное исключение из <c>ExecuteAsync</c> по умолчанию останавливает
            // хост целиком — недоступная на минуту база уронила бы весь API.
            // Проход не удался, повторим через сутки
            logger.LogError(e, "Daily cleanup failed");
        }
    }
}
