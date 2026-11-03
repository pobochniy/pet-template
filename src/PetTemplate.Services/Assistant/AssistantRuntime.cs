using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PetTemplate.Services.Assistant.Hooks;
using PetTemplate.Services.Assistant.Models;
using PetTemplate.Services.Assistant.Plans;
using PetTemplate.Services.Assistant.Sections;
using PetTemplate.Services.Assistant.Tools;
using PetTemplate.Services.Billing;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Единственная сборка хода ассистента (спец. assistant, 15.2): всё, без чего ход
/// не идёт, — модели, размыкатель, инструменты, план, промпт, история, память, трасса
/// и учёт. Её берут три хозяина: приложение (<c>ServicesExtensions.AddAssistant</c>),
/// мир сверки модели (<c>ModelCheckRunner</c>, спец. admin, 15.3) и мир тестов
/// (<c>AssistantWorld</c>). Отличия хозяев — регистрациями поверх
/// (<see cref="ServiceCollectionDescriptorExtensions.Replace"/>), а не своей копией
/// списка: новый сервис хода, добавленный здесь, появляется у всех трёх сразу.
///
/// Разделы проекта — <see cref="AddAssistantSection{TSection}"/>: одна регистрация на раздел
/// </summary>
public static class AssistantRuntime
{
    public static IServiceCollection AddAssistantRuntime(this IServiceCollection services)
    {
        // Клиенты моделей, метрики, разбор отказов поставщика и сигнал долгого опроса —
        // синглтоны: клиенты держат соединения, метр один на процесс (13.4, 16.5)
        services.TryAddSingleton<IModelChatClients, ModelChatClients>();
        services.TryAddSingleton(_ => new AssistantMetrics());
        services.TryAddSingleton<IProviderErrorClassifier>(OpenAiErrorClassifier.Instance);
        services.TryAddSingleton<RunNotifier>();
        services.TryAddSingleton(_ => new BillingMetrics());
        services.TryAddSingleton<TraceSummary>();

        services.AddTransient<AiModelCatalog>();
        services.AddTransient<ModelBreaker>();

        // Контекст хода, копилка плана, исход и предохранители — на область команды:
        // в них лежит, от чьего имени идёт ход и что он успел задумать
        services.AddScoped<AssistantContext>();
        services.AddScoped<PlanCollector>();
        services.AddScoped<RunOutcome>();
        services.AddScoped<LoopGuards>();
        services.AddScoped<AssistantTools>();
        services.AddScoped<AssistantRunService>();
        services.AddTransient<MemoryService>();
        services.AddTransient<PlanOperations>();
        services.AddTransient<PlanExecutor>();
        services.AddTransient<AssistantPermissions>();
        services.AddTransient<SystemPromptBuilder>();
        services.AddTransient<HistoryBuilder>();

        // Трасса и учёт расхода (13.1, 13.5): тот же контекст, что у хода — трасса
        // сохраняет и его накопленные токены, учёт уезжает сохранением финального статуса
        services.AddScoped<TraceWriter>();
        services.AddScoped<IRunTrace>(sp => sp.GetRequiredService<TraceWriter>());
        services.AddScoped<IRunBilling, RunBilling>();

        return services;
    }

    /// <summary>
    /// Раздел проекта для ассистента (спец. assistant, 7.1): описание — синглтоном, его
    /// инструменты, операции и сервисы — тем, что он сам регистрирует в <c>Register</c>
    /// </summary>
    public static IServiceCollection AddAssistantSection<TSection>(this IServiceCollection services)
        where TSection : class, IAssistantSection, new() =>
        services.AddAssistantSection(new TSection());

    /// <summary>
    /// Тот же раздел готовым экземпляром — так мир сверки повторяет разделы приложения,
    /// а тест подставляет раздел со своим хранилищем
    /// </summary>
    public static IServiceCollection AddAssistantSection(this IServiceCollection services, IAssistantSection section)
    {
        services.AddSingleton(section);
        section.Register(new AssistantSectionBuilder(services));

        return services;
    }
}
