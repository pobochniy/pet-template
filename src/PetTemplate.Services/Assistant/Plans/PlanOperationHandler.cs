using System.Text.Json;
using PetTemplate.Shared.Dto.Assistant;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Assistant.Plans;

/// <summary>
/// Основа операции плана с типизированными аргументами (спец. assistant, 7.1).
///
/// Умолчания — осторожные: операция создаёт, применяется сразу только в границах
/// <c>ImmediateLimit</c>, ссылок не имеет и права не требует сверх участия
/// в пространстве. Раздел переопределяет то, что у него иначе.
/// </summary>
/// <typeparam name="TArgs">Аргументы операции — то, что инструмент кладёт в план</typeparam>
public abstract class PlanOperationHandler<TArgs> : IPlanOperationHandler
    where TArgs : class
{
    public abstract string Kind { get; }

    public virtual bool IsCreating => true;

    public virtual bool CanApplyImmediately => true;

    public virtual int? MaxPerPlan => null;

    public virtual IReadOnlyCollection<string> Frames => [];

    public virtual IReadOnlyList<PermissionEnum> Permissions => [];

    public string? Target(JsonElement args) => Target(Read(args));

    public Task<string?> Check(JsonElement args, AssistantContext context, CancellationToken ct) =>
        Check(Read(args), context, ct);

    public Task<string> Apply(JsonElement args, AssistantContext context, CancellationToken ct) =>
        Apply(Read(args), context, ct);

    /// <summary>Правка существующего — её идентификатор; у создания — <c>null</c></summary>
    protected virtual string? Target(TArgs args) => null;

    /// <summary>Сверка с базой перед применением (5.4); по умолчанию сверять нечего</summary>
    protected virtual Task<string?> Check(TArgs args, AssistantContext context, CancellationToken ct) =>
        Task.FromResult<string?>(null);

    protected abstract Task<string> Apply(TArgs args, AssistantContext context, CancellationToken ct);

    private static TArgs Read(JsonElement args) =>
        PlanJson.FromArgs<TArgs>(args) ?? throw new InvalidOperationException("Plan operation has no arguments");
}
