using Microsoft.Extensions.AI;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Billing;

/// <summary>
/// Строка служебного расхода (спец. assistant, 13.5): обращение к поставщику не ходом
/// человека. Цена — той же формулой, что ход (<see cref="RunBilling.Cost"/>),
/// и по цене модели на момент вызова: прайс, поправленный завтра, сегодняшнюю
/// строку не перепишет. У расшифровки — по минутам (<see cref="Transcription"/>)
/// </summary>
public static class ServiceSpending
{
    public static ServiceSpend Of(
        ServiceSpendKind kind,
        AiModel model,
        long tokensIn,
        long tokensCached,
        long tokensOut,
        Guid? workspaceId = null) => new()
    {
        Id = Guid.CreateVersion7(),
        Kind = kind,
        ModelId = model.Id,
        Provider = model.Provider,
        ModelKey = model.Key,
        TokensIn = tokensIn,
        TokensCached = tokensCached,
        TokensOut = tokensOut,
        Cost = RunBilling.Cost(tokensIn, tokensCached, tokensOut, model),
        CreatedUtc = DateTime.UtcNow,
        WorkspaceId = workspaceId
    };

    /// <summary>По <c>usage</c> одного ответа</summary>
    public static ServiceSpend Of(
        ServiceSpendKind kind,
        AiModel model,
        UsageDetails? usage,
        Guid? workspaceId = null) =>
        Of(kind, model,
            usage?.InputTokenCount ?? 0,
            usage?.CachedInputTokenCount ?? 0,
            usage?.OutputTokenCount ?? 0,
            workspaceId);

    /// <summary>
    /// Расшифровка голоса (раздел 6): модели справочника у неё нет, поэтому поставщик
    /// и модель — строками из конфигурации, а цена — минуты записи по
    /// <c>Transcription.CostPerMinute</c> на момент вызова. Пространство и человек
    /// записаны: эта строка идёт в лимит пространства (13.6)
    /// </summary>
    public static ServiceSpend Transcription(
        AssistantTranscriptionOptions settings,
        Guid workspaceId,
        Guid userId,
        int seconds,
        long tokensIn,
        long tokensOut) => new()
    {
        Id = Guid.CreateVersion7(),
        Kind = ServiceSpendKind.Transcription,
        Provider = settings.Provider,
        ModelKey = settings.Model,
        TokensIn = tokensIn,
        TokensOut = tokensOut,
        Seconds = seconds,
        Cost = Math.Round(seconds * settings.CostPerMinute / 60m, 6),
        CreatedUtc = DateTime.UtcNow,
        WorkspaceId = workspaceId,
        UserId = userId
    };
}
