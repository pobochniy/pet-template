using Microsoft.Extensions.AI;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Assistant.Tools;

/// <summary>
/// Инструмент раздела вместе с правами, которые он требует (спец. assistant, 7.1 и раздел 9), —
/// как <c>IPlanOperationHandler.Permissions</c> у операции плана.
///
/// Права объявляются, а не проверяются в теле инструмента: каталог оборачивает
/// инструмент в <see cref="PermittedFunction"/>, и тот спрашивает базу перед каждым вызовом.
/// Забыть проверку нельзя — без набора прав инструмент не построить
/// </summary>
/// <param name="Function">Сам инструмент: имя, описание, схема</param>
/// <param name="Permissions">
/// Хотя бы одно из прав в пространстве — тот же набор, что на эндпоинте раздела
/// (у чтения — <c>Crud</c> и <c>Read</c>). Пусто — «участник пространства»
/// </param>
public sealed record AssistantTool(AIFunction Function, IReadOnlyList<PermissionEnum> Permissions)
{
    public string Name => Function.Name;

    /// <summary>
    /// Инструмент из метода: описания — <c>[Description]</c> на методе и параметрах,
    /// схема — <see cref="AssistantTools.SchemaJson"/>
    /// </summary>
    public static AssistantTool Create(Delegate method, string name, params PermissionEnum[] anyOf) =>
        new(AIFunctionFactory.Create(method, name, null, AssistantTools.SchemaJson), anyOf);
}
