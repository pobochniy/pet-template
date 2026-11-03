using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Services.Assistant.Plans;
using PetTemplate.Services.Assistant.Tools;

namespace PetTemplate.Services.Assistant.Sections;

/// <summary>
/// Что раздел приносит в контейнер (спец. assistant, 7.1): источник инструментов,
/// операции плана и свои сервисы. Тот же вызов собирает и приложение, и мир сверки модели
/// </summary>
public sealed class AssistantSectionBuilder(IServiceCollection services)
{
    /// <summary>Контейнер — для сервисов раздела, которые зовут его инструменты и операции</summary>
    public IServiceCollection Services => services;

    /// <summary>Источник инструментов раздела — scoped, как всё, что живёт в ходе</summary>
    public AssistantSectionBuilder Tools<TSource>() where TSource : class, IAssistantToolSource
    {
        services.AddScoped<IAssistantToolSource, TSource>();

        return this;
    }

    /// <summary>
    /// Операция плана — то, что исполнит <c>apply_plan</c> после согласия. Пока ни одной
    /// нет, <c>apply_plan</c> модели не показывается
    /// </summary>
    public AssistantSectionBuilder Operation<THandler>() where THandler : class, IPlanOperationHandler
    {
        services.AddScoped<IPlanOperationHandler, THandler>();

        return this;
    }
}
