using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Admin;

/// <summary>
/// Справочник моделей в админке (спец. admin, 15.1 и 15.2): цены по прайсу поставщика,
/// включение и порядок. Удаления нет — только выключение: на модель ссылаются прогоны
/// и строки расхода.
///
/// Отказы — текстом, без кодов: админка их не переводит (спец. admin, 3.5)
/// </summary>
public class AdminModelService(
    ApplicationContext db,
    AssistantMetrics metrics,
    IOptions<AssistantOptions> assistant,
    IOptions<SpendOptions> spend)
{
    /// <summary>Админов один-два: третьего одновременного создания не бывает, это запас</summary>
    private const int CreateAttempts = 3;

    /// <summary>Имя уникального индекса порядка — snake_case, как его дал соглашение базы</summary>
    private const string PriorityIndex = "ix_ai_models_priority";

    public async Task<AdminModelsDto> List(CancellationToken ct = default)
    {
        var models = await db.AiModels
            .AsNoTracking()
            .OrderBy(x => x.Priority)
            .Select(x => new AdminModelDto
            {
                Id = x.Id,
                Provider = x.Provider,
                Key = x.Key,
                DisplayName = x.DisplayName,
                Priority = x.Priority,
                CostIn = x.CostIn,
                CostCached = x.CostCached,
                CostOut = x.CostOut,
                IsEnabled = x.IsEnabled,
                IsLocal = x.IsLocal,
                OpenUntilUtc = x.OpenUntilUtc,
                FailureCount = x.FailureCount,
                IsUsed = db.AssistantRuns.Any(r => r.AiModelId == x.Id)
                         || db.ServiceSpend.Any(s => s.ModelId == x.Id),
                UpdatedUtc = x.UpdatedUtc,
                UpdatedBy = db.Profiles
                    .Where(p => p.Id == x.UpdatedByUserId)
                    .Select(p => p.UserName)
                    .FirstOrDefault(),
                Version = x.Version
            })
            .ToArrayAsync(ct);

        // Модель распознавания — не строка справочника (спец. assistant, 6.2): правится
        // переменной окружения, цена у неё за минуту. Экран показывает её строкой
        // под таблицей, чтобы «во что обходится ассистент» читалось целиком
        var transcription = assistant.Value.Transcription;

        return new AdminModelsDto
        {
            Currency = spend.Value.Currency,
            Models = models,
            Transcription = string.IsNullOrWhiteSpace(transcription.Model)
                ? null
                : new AdminTranscriptionModelDto
                {
                    Provider = transcription.Provider,
                    Model = transcription.Model,
                    CostPerMinute = transcription.CostPerMinute
                }
        };
    }

    /// <summary>
    /// Новая модель встаёт последней: куда её поставить, решает перестановка.
    ///
    /// Номер — «последний + 1», и два одновременных создания посчитали бы один и тот же:
    /// второе упирается в уникальный индекс порядка. Блокировки ради этого не держим —
    /// проигравший пересчитывает номер и сохраняет ещё раз, до <see cref="CreateAttempts"/> раз
    /// </summary>
    public async Task<Guid> Create(AdminModelEditDto dto, Guid adminId, CancellationToken ct = default)
    {
        Validate(dto);

        var model = new AiModel
        {
            Id = Guid.CreateVersion7(),
            Provider = string.Empty,
            Key = string.Empty,
            DisplayName = string.Empty
        };
        Fill(model, dto, adminId);
        db.AiModels.Add(model);

        var attempt = 0;
        while (true)
        {
            attempt++;
            model.Priority = (await db.AiModels.MaxAsync(x => (int?)x.Priority, ct) ?? 0) + 1;

            try
            {
                await db.SaveChangesAsync(ct);
                return model.Id;
            }
            catch (DbUpdateException e) when (attempt < CreateAttempts && DbErrors.IsDuplicate(e, PriorityIndex))
            {
                // Строка осталась в трекере добавленной — следующий проход сохранит её с новым номером
            }
        }
    }

    /// <summary>
    /// Правка цены прошлого не переписывает: строки расхода уже записали стоимость
    /// по тогдашнему прайсу (спец. assistant, 13.5). Поставщик и ключ у модели, по
    /// которой ходили, не меняются вовсе. Строку, которую поправили после того, как
    /// экран её прочитал (<see cref="AdminModelEditDto.Version"/>), правка не перезапишет.
    /// <c>false</c> — такой модели нет
    /// </summary>
    public async Task<bool> Update(Guid id, AdminModelEditDto dto, Guid adminId, CancellationToken ct = default)
    {
        Validate(dto);

        var model = await db.AiModels.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (model == null) return false;

        // Поставщик и ключ — это сама модель: по ним прошлые ходы уходят к судье
        // (16.3), а траты — в сводку. Переписать их у модели, по которой уже ходили,
        // значит переписать историю, поэтому другая модель — это новая строка
        var replaced = model.Provider != dto.Provider.Trim() || model.Key != dto.Key.Trim();
        if (replaced && await Used(id, ct))
            throw new AdminRejectionException("По этой модели уже ходили — поставщика и ключ не поменять. Заведите новую модель");

        var key = model.Key;
        Fill(model, dto, adminId);

        // Сверка — по версии, которую видел экран, а не по только что прочитанной
        db.Entry(model).Property(x => x.Version).OriginalValue = dto.Version;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AdminRejectionException("Модель уже поменялась — откройте справочник заново и повторите правку");
        }

        // Прибор состояния (спец. assistant, 13.4) знает модель по ключу: выключенная или
        // переименованная больше не ходит, и её последнее «разомкнута» висело бы ложной тревогой
        if (!model.IsEnabled || model.Key != key) metrics.ForgetModel(key);

        return true;
    }

    /// <summary>
    /// Порядок целиком (15.2): клиент присылает все модели в новом порядке, сервер
    /// переписывает номера с единицы. Список, в котором модели не хватает или она
    /// лишняя, — это экран, открытый до чужой правки, и молча дополнять его нельзя.
    ///
    /// Уникальный индекс Postgres без <c>DEFERRABLE</c> проверяет каждую строку сразу,
    /// а не в конце транзакции, поэтому обмен «1 ↔ 2» в лоб на нём падает. Сначала все
    /// номера уходят в отрицательные — они заведомо свободны, — и уже оттуда на свои
    /// места. Запросов на запись несколько, отсюда явная транзакция: снаружи никто
    /// не увидит справочник с отрицательным порядком
    /// </summary>
    public async Task Reorder(Guid[] ids, Guid adminId, CancellationToken ct = default)
    {
        var current = await db.AiModels
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Priority, ct);

        if (ids.Length != current.Count || ids.Distinct().Count() != ids.Length || !Array.TrueForAll(ids, current.ContainsKey))
            throw new AdminRejectionException(
                "Порядок должен перечислять все модели справочника ровно по разу — обновите экран: справочник уже поменялся");

        var now = DateTime.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.AiModels.ExecuteUpdateAsync(x => x.SetProperty(m => m.Priority, m => -m.Priority), ct);

        for (var i = 0; i < ids.Length; i++)
        {
            var id = ids[i];
            var priority = i + 1;
            var rows = db.AiModels.Where(x => x.Id == id);

            // Кто правил — только у тех, чей номер и правда сменился
            if (current[id] == priority)
                await rows.ExecuteUpdateAsync(x => x.SetProperty(m => m.Priority, priority), ct);
            else
                await rows.ExecuteUpdateAsync(x => x
                    .SetProperty(m => m.Priority, priority)
                    .SetProperty(m => m.UpdatedUtc, now)
                    .SetProperty(m => m.UpdatedByUserId, adminId), ct);
        }

        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Замкнуть руками (спец. assistant, 16.2): отмена идущей паузы, когда видно, что модель
    /// ожила. Выбором модели для хода это не является и порядка не меняет. Цен и включения
    /// не трогает, поэтому и «кто правил» остаётся прежним: кто замкнул, видно метрикой
    /// <c>closed_by_admin</c>. Пишет атомарным <c>UPDATE</c>, как и сам размыкатель, —
    /// токен правки ему не нужен. <c>false</c> — такой модели нет
    /// </summary>
    public async Task<bool> Close(Guid id, CancellationToken ct = default)
    {
        var key = await db.AiModels
            .Where(x => x.Id == id)
            .Select(x => x.Key)
            .SingleOrDefaultAsync(ct);
        if (key == null) return false;

        await db.AiModels
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(x => x
                .SetProperty(m => m.FailureCount, 0)
                .SetProperty(m => m.OpenUntilUtc, (DateTime?)null)
                .SetProperty(m => m.OpenForSeconds, 0), ct);

        metrics.Breaker(key, BreakerEvents.ClosedByAdmin);
        metrics.ModelState(key, false);

        return true;
    }

    /// <summary>
    /// По модели ходили или она отвечала в служебном вызове: прогон, его расход, компакция,
    /// сверка, оценка. Запуск и оценка пишут служебный расход — его проверки хватает
    /// </summary>
    private async Task<bool> Used(Guid id, CancellationToken ct) =>
        await db.AssistantRuns.AnyAsync(x => x.AiModelId == id, ct)
        || await db.RunSpend.AnyAsync(x => x.ModelId == id, ct)
        || await db.ServiceSpend.AnyAsync(x => x.ModelId == id, ct)
        || await db.AdminJobs.AnyAsync(x => x.ModelId == id, ct);

    private static void Fill(AiModel model, AdminModelEditDto dto, Guid adminId)
    {
        model.Provider = dto.Provider.Trim();
        model.Key = dto.Key.Trim();
        model.DisplayName = dto.DisplayName.Trim();
        model.CostIn = dto.CostIn;
        model.CostCached = dto.CostCached;
        model.CostOut = dto.CostOut;
        model.IsEnabled = dto.IsEnabled;
        model.IsLocal = dto.IsLocal;
        model.UpdatedUtc = DateTime.UtcNow;
        model.UpdatedByUserId = adminId;
    }

    /// <summary>
    /// Нулевая цена в шаблоне законна (спец. assistant, 13.5): токены считаются, деньги
    /// нет — так заводят свою карту, пока себестоимость не прикинута. Отрицательная — опечатка
    /// </summary>
    private static void Validate(AdminModelEditDto dto)
    {
        Require(dto.Provider, "Поставщик", 64);
        Require(dto.Key, "Ключ модели", 256);
        Require(dto.DisplayName, "Название", 128);

        if (dto.CostIn < 0 || dto.CostCached < 0 || dto.CostOut < 0)
            throw new AdminRejectionException("Цена не может быть отрицательной");
    }

    private static void Require(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new AdminRejectionException($"Не заполнено поле «{field}»");
        if (value.Trim().Length > maxLength)
            throw new AdminRejectionException($"Поле «{field}» длиннее {maxLength} символов");
    }
}
