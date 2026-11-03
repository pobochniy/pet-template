using PetTemplate.Db.Entity;

namespace PetTemplate.Services.Assistant.Hooks;

/// <summary>Учёта нет: токены остаются в строке прогона. Для тестов, которым учёт не нужен</summary>
public sealed class NullRunBilling : IRunBilling
{
    public bool ShouldCharge(AssistantRun run) => false;

    public Task Charge(AssistantRun run, CancellationToken ct) => Task.CompletedTask;
}
