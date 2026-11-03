using Microsoft.Extensions.AI;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Assistant.Tools;

/// <summary>
/// Инструмент раздела за проверкой прав (спец. assistant, раздел 9): перед каждым вызовом —
/// живые права автора хода в пространстве хода (<see cref="AssistantPermissions"/>), без
/// снимка и кэша: право отбирают и посреди хода. Нет права — модели уходит строка отказа,
/// а инструмент не зовётся вовсе. Обёртку ставит каталог (<c>AssistantTools.Catalog</c>),
/// раздел только объявляет набор (<see cref="AssistantTool"/>)
/// </summary>
public sealed class PermittedFunction(
    AIFunction inner,
    IReadOnlyList<PermissionEnum> permissions,
    AssistantPermissions access,
    AssistantContext context) : DelegatingAIFunction(inner)
{
    /// <summary>Отказ — строкой модели, как любой отказ инструмента (раздел 7)</summary>
    public const string Denied = "нет права: у человека нет доступа к этому в пространстве — " +
                                 "скажи ему об этом и не пытайся обойти другим инструментом";

    public IReadOnlyList<PermissionEnum> Permissions => permissions;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken ct)
    {
        if (!await access.Has(context.UserId, context.WorkspaceId, ct, [.. permissions])) return Denied;

        return await base.InvokeCoreAsync(arguments, ct);
    }
}
