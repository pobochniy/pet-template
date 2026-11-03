using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Options;

namespace PetTemplate.Api.Workers;

/// <summary>
/// Стартовый справочник моделей ассистента (спец. assistant, 16.2 и 17): в пустую
/// таблицу <c>ai_models</c> на старте API ложатся строки <c>AssistantOptions.SeedModels</c>.
///
/// Сид в миграции не годится: поставщик и ключ модели — настройка окружения проекта,
/// а не схемы. А экран «Модели» (спец. admin, 15) ждёт админа с ролью и открытого замка
/// админки — без сида свежая установка до этого жила бы с ассистентом, которому некуда
/// идти. Справочник не пуст — конфигурация не читается вовсе: дальше моделями владеет
/// админка, и правка переменной окружения не перепишет то, что там решили.
///
/// Строка без поставщика или ключа модели не заводится: модель «ни у кого» дала бы
/// только отказ поставщика на каждом ходе. Не вышло (таблицы нет — миграции
/// не накачены) — пишем в лог и поднимаемся: ассистент недоступен, остальное работает
/// </summary>
public sealed class AiModelSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<AssistantOptions> options,
    ILogger<AiModelSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value.SeedModels
            .Where(x => !string.IsNullOrWhiteSpace(x.Provider) && !string.IsNullOrWhiteSpace(x.Key))
            .ToArray();
        if (seed.Length == 0) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            if (await db.AiModels.AnyAsync(cancellationToken)) return;

            var now = DateTime.UtcNow;
            db.AiModels.AddRange(seed.Select(x => new AiModel
            {
                Id = Guid.CreateVersion7(now),
                Provider = x.Provider,
                Key = x.Key,
                DisplayName = string.IsNullOrWhiteSpace(x.DisplayName) ? x.Key : x.DisplayName,
                Priority = x.Priority,
                CostIn = x.CostIn,
                CostCached = x.CostCached,
                CostOut = x.CostOut,
                IsEnabled = true,
                UpdatedUtc = now
            }));
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Assistant model catalog seeded with {Count} models", seed.Length);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            // Два экземпляра API на пустой базе: второй упрётся в уникальный порядок —
            // справочник уже завёл первый
            logger.LogError(e, "Assistant model catalog was not seeded");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
