using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Admin;
using PetTemplate.Services.Assistant;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Справочник моделей в админке (спец. admin, 15.1): чтение — любому админу, правка —
/// роли <c>Models</c>. Перестановка порядка и гонки правки — <see cref="AdminModelsDbTests"/>
/// </summary>
public class AdminModelsApiTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    /// <summary>Свой на каждый тест: xUnit заводит экземпляр класса на тест</summary>
    private readonly PetTemplate.Db.Entity.Profile _oleg = new UserBuilder("Oleg").Please().profile;

    private readonly AiModel _seed = AssistantWorld.AiModel("Основная", 1, Now);

    /// <summary>
    /// Экран моделей открыт на чтение всякому админу: цены, валюта прайса и модель
    /// распознавания голоса — она не строка справочника, но тоже стоит денег
    /// </summary>
    [Fact]
    public async Task AnyAdminCanSeeModels()
    {
        // Arrange
        _seed.CostIn = 0.3m;
        var (client, _) = await Client(AdminRole.Accounts, settings: new Dictionary<string, string?>
        {
            ["AssistantOptions:Transcription:Model"] = "whisper-1",
            ["AssistantOptions:Transcription:CostPerMinute"] = "0.6",
            ["SpendOptions:Currency"] = "RUB"
        });

        // Act
        var response = await client.GetAsync("/api/Admin/Models");

        // Assert
        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AdminModelsDto>();
        Assert.Equal("RUB", result!.Currency);
        var seed = Assert.Single(result.Models);
        Assert.Equal((_seed.Id, 0.3m, false), (seed.Id, seed.CostIn, seed.IsUsed));
        Assert.Null(seed.UpdatedBy);
        Assert.Equal(("whisper-1", 0.6m), (result.Transcription!.Model, result.Transcription.CostPerMinute));
    }

    /// <summary>Новая модель встаёт последней по порядку, и видно, кто её завёл</summary>
    [Fact]
    public async Task CanAddModel()
    {
        // Arrange
        var (client, db) = await Client(AdminRole.Models);

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Models", Local());

        // Assert
        await response.ShouldBeSuccessful();
        var id = await response.Content.ReadFromJsonAsync<Guid>();
        var model = await db.AiModels.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(2, model.Priority);
        Assert.Equal("Локальная 3090", model.DisplayName);
        Assert.True(model.IsLocal);
        Assert.Equal(_oleg.Id, model.UpdatedByUserId);
    }

    /// <summary>Нулевая цена законна (спец. assistant, 13.5): токены считаются, деньги нет</summary>
    [Fact]
    public async Task CanAddFreeModel()
    {
        // Arrange
        var (client, _) = await Client(AdminRole.Models);
        var free = Local();
        free.CostIn = free.CostCached = free.CostOut = 0;

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Models", free);

        // Assert
        await response.ShouldBeSuccessful();
    }

    [Fact]
    public async Task ReturnsNotFoundOnAddWithoutModelsRole()
    {
        // Arrange
        var (client, db) = await Client(AdminRole.Accounts);

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Models", Local());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, await db.AiModels.CountAsync());
    }

    /// <summary>Правка цены — с отметкой, кто правил</summary>
    [Fact]
    public async Task CanChangePrice()
    {
        // Arrange
        var (client, db) = await Client(AdminRole.Models);

        // Act
        var response = await client.PutAsJsonAsync($"/api/Admin/Models/{_seed.Id}", await Edit(db, costIn: 0.4m));

        // Assert
        await response.ShouldBeSuccessful();
        var model = await db.AiModels.AsNoTracking().SingleAsync();
        Assert.Equal(0.4m, model.CostIn);
        Assert.Equal(_oleg.Id, model.UpdatedByUserId);
    }

    /// <summary>
    /// Правка цены прошлого не переписывает: строка расхода хода хранит стоимость
    /// по тогдашнему прайсу (спец. assistant, 13.5)
    /// </summary>
    [Fact]
    public async Task PriceChangeKeepsRecordedSpend()
    {
        // Arrange
        var (client, db) = await Client(AdminRole.Models, x => x.RunSpend.Add(Spend(1.5m)));

        // Act
        var response = await client.PutAsJsonAsync($"/api/Admin/Models/{_seed.Id}", await Edit(db, costIn: 10m));

        // Assert
        await response.ShouldBeSuccessful();
        Assert.Equal(1.5m, (await db.RunSpend.AsNoTracking().SingleAsync()).Cost);
    }

    /// <summary>
    /// Поставщик и ключ — это сама модель: по которой уже ходили, ту не переписать —
    /// иначе прошлые ходы ушли бы к чужому судье, а траты — под другой ключ (16.3)
    /// </summary>
    [Fact]
    public async Task CannotReplaceProviderOfUsedModel()
    {
        // Arrange
        var (client, db) = await Client(AdminRole.Models, x => x.RunSpend.Add(Spend(1.5m)));
        var edit = await Edit(db);
        edit.Provider = "yandex";

        // Act
        var response = await client.PutAsJsonAsync($"/api/Admin/Models/{_seed.Id}", edit);

        // Assert
        await response.ShouldBeBadRequest("По этой модели уже ходили — поставщика и ключ не поменять. Заведите новую модель");
        Assert.Equal("main", (await db.AiModels.AsNoTracking().SingleAsync()).Provider);
    }

    /// <summary>По модели ещё не ходили — ключ можно поправить, например опечатку</summary>
    [Fact]
    public async Task CanFixKeyOfUnusedModel()
    {
        // Arrange
        var (client, db) = await Client(AdminRole.Models);
        var edit = await Edit(db);
        edit.Key = "qwen3-32b";

        // Act
        var response = await client.PutAsJsonAsync($"/api/Admin/Models/{_seed.Id}", edit);

        // Assert
        await response.ShouldBeSuccessful();
        Assert.Equal("qwen3-32b", (await db.AiModels.AsNoTracking().SingleAsync()).Key);
    }

    /// <summary>Выключение — единственный способ убрать модель: удаления нет (15.1)</summary>
    [Fact]
    public async Task CanTurnModelOff()
    {
        // Arrange
        var (client, db) = await Client(AdminRole.Models);

        // Act
        var response = await client.PutAsJsonAsync($"/api/Admin/Models/{_seed.Id}", await Edit(db, enabled: false));

        // Assert
        await response.ShouldBeSuccessful();
        Assert.False((await db.AiModels.AsNoTracking().SingleAsync()).IsEnabled);
    }

    /// <summary>
    /// Экран, открытый до чужой правки, правку не перезапишет: версия строки (<c>xmin</c>)
    /// уже другая — отказ «откройте заново», а цена остаётся той, что поставил первый
    /// </summary>
    [Fact]
    public async Task StaleEditIsRefused()
    {
        // Arrange
        var (client, db) = await Client(AdminRole.Models);
        var stale = await Edit(db, costIn: 0.4m);
        await (await client.PutAsJsonAsync($"/api/Admin/Models/{_seed.Id}", await Edit(db, costIn: 0.7m)))
            .ShouldBeSuccessful();

        // Act
        var response = await client.PutAsJsonAsync($"/api/Admin/Models/{_seed.Id}", stale);

        // Assert
        await response.ShouldBeBadRequest("Модель уже поменялась — откройте справочник заново и повторите правку");
        Assert.Equal(0.7m, (await db.AiModels.AsNoTracking().SingleAsync()).CostIn);
    }

    [Fact]
    public async Task RefusesNegativePrice()
    {
        // Arrange
        var (client, _) = await Client(AdminRole.Models);
        var model = Local();
        model.CostOut = -1;

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Models", model);

        // Assert
        await response.ShouldBeBadRequest("Цена не может быть отрицательной");
    }

    [Fact]
    public async Task RefusesModelWithoutKey()
    {
        // Arrange
        var (client, _) = await Client(AdminRole.Models);
        var model = Local();
        model.Key = " ";

        // Act
        var response = await client.PostAsJsonAsync("/api/Admin/Models", model);

        // Assert
        await response.ShouldBeBadRequest("Не заполнено поле «Ключ модели»");
    }

    [Fact]
    public async Task ReturnsNotFoundOnMissingModel()
    {
        // Arrange
        var (client, db) = await Client(AdminRole.Models);

        // Act
        var response = await client.PutAsJsonAsync($"/api/Admin/Models/{Guid.CreateVersion7()}", await Edit(db));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Замкнуть руками (спец. assistant, 16.2): пауза снята, счётчик обнулён, а цены,
    /// порядок и «кто правил» — как были: это отмена паузы, а не правка модели
    /// </summary>
    [Fact]
    public async Task CanCloseOpenModel()
    {
        // Arrange
        Open(_seed);
        var (client, db) = await Client(AdminRole.Models);

        // Act
        var response = await client.PostAsync($"/api/Admin/Models/{_seed.Id}/Close", null);

        // Assert
        await response.ShouldBeSuccessful();
        var model = await db.AiModels.AsNoTracking().SingleAsync();
        Assert.Equal(((DateTime?)null, 0, 0), (model.OpenUntilUtc, model.FailureCount, model.OpenForSeconds));
        Assert.Null(model.UpdatedByUserId);
    }

    /// <summary>Состояние размыкателя видно на экране: до какого момента и сколько отказов</summary>
    [Fact]
    public async Task ListShowsBreakerState()
    {
        // Arrange
        var until = Open(_seed);
        var (client, _) = await Client(AdminRole.Accounts);

        // Act
        var response = await client.GetAsync("/api/Admin/Models");

        // Assert
        await response.ShouldBeSuccessful();
        var seed = Assert.Single((await response.Content.ReadFromJsonAsync<AdminModelsDto>())!.Models);
        Assert.Equal(until, seed.OpenUntilUtc!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(3, seed.FailureCount);
    }

    [Fact]
    public async Task ReturnsNotFoundOnCloseWithoutModelsRole()
    {
        // Arrange
        Open(_seed);
        var (client, db) = await Client(AdminRole.Accounts);

        // Act
        var response = await client.PostAsync($"/api/Admin/Models/{_seed.Id}/Close", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull((await db.AiModels.AsNoTracking().SingleAsync()).OpenUntilUtc);
    }

    [Fact]
    public async Task ReturnsNotFoundOnClosingMissingModel()
    {
        // Arrange
        var (client, _) = await Client(AdminRole.Models);

        // Act
        var response = await client.PostAsync($"/api/Admin/Models/{Guid.CreateVersion7()}/Close", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Сверить модель — роль <c>Models</c>: без неё запуска нет</summary>
    [Fact]
    public async Task ReturnsNotFoundOnCheckWithoutModelsRole()
    {
        // Arrange
        var (client, db) = await Client(AdminRole.Accounts);

        // Act
        var response = await client.PostAsync($"/api/Admin/Models/{_seed.Id}/Check", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await db.AdminJobs.AnyAsync());
    }

    /// <summary>
    /// «Сверить» отвечает запуском, который экран перечитывает; список сверок открыт
    /// любому админу. Разбирает запуск админский воркер очереди — воркера у теста нет
    /// </summary>
    [Fact]
    public async Task CheckStartsJob()
    {
        // Arrange
        var (client, _) = await Client(AdminRole.Models);

        // Act
        var response = await client.PostAsync($"/api/Admin/Models/{_seed.Id}/Check", null);

        // Assert
        await response.ShouldBeSuccessful();
        var job = await response.Content.ReadFromJsonAsync<AdminJobDto>();
        Assert.Equal((AdminJobKind.ModelCheck, AdminJobStatus.Running, "Oleg"), (job!.Kind, job.Status, job.StartedBy));
        Assert.Equal(0, job.Report!.Results.Length);
        var checks = await client.GetFromJsonAsync<AdminJobDto[]>("/api/Admin/Models/Checks");
        Assert.Equal(job.Id, Assert.Single(checks!).Id);
    }

    /// <summary>
    /// Выключенная модель уходит из прибора состояния сразу при правке (спец. assistant,
    /// 13.4), а не на ближайшем выборе модели: иначе её последнее «разомкнута» висело бы
    /// ложной тревогой, пока кто-нибудь не напишет ассистенту
    /// </summary>
    [Fact]
    public async Task TurningModelOffRemovesItFromOpenGauge()
    {
        // Arrange: метр свой — метр продукта есть у каждого хоста, а тесты идут параллельно
        var meter = "PetTemplate.Assistant.Test." + Guid.NewGuid().ToString("N");
        using var metrics = new AssistantMetrics(meter);
        var (_, db) = await Client(AdminRole.Models);
        var service = new AdminModelService(db, metrics,
            Options.Create(new AssistantOptions()), Options.Create(new SpendOptions()));
        metrics.ModelState(_seed.Key, true);
        metrics.ModelState("kept", false);

        // Act
        await service.Update(_seed.Id, await Edit(db, enabled: false), _oleg.Id);

        // Assert
        var seen = OpenGauge(meter);
        Assert.Contains("kept", seen);
        Assert.DoesNotContain(_seed.Key, seen);
    }

    /// <summary>Какие модели сейчас в приборе состояния размыкателя</summary>
    private static string[] OpenGauge(string meter)
    {
        var seen = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == meter && instrument.Name == "pettemplate.assistant.model.open")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "model") seen.Enqueue((string)tag.Value!);
        });
        listener.Start();
        listener.RecordObservableInstruments();

        return seen.ToArray();
    }

    /// <summary>Модель разомкнута размыкателем после трёх отказов</summary>
    private static DateTime Open(AiModel model)
    {
        var until = DateTime.UtcNow.AddMinutes(1);
        model.FailureCount = 3;
        model.OpenForSeconds = 60;
        model.OpenUntilUtc = until;

        return until;
    }

    private async Task<(HttpClient client, ApplicationContext db)> Client(
        AdminRole role,
        Action<ApplicationContext>? arrange = null,
        IReadOnlyDictionary<string, string?>? settings = null) =>
        await Given.AdminClient(_oleg.Id, [role], db =>
        {
            db.Profiles.Add(_oleg);
            db.AiModels.Add(_seed);
            db.UserInAdminRoles.AddRange(Given.AdminRoles(_oleg.Id, role));
            arrange?.Invoke(db);
        }, settings);

    /// <summary>Ход по стартовой модели — уже записанный расход</summary>
    private RunSpend Spend(decimal cost) => new()
    {
        Id = Guid.CreateVersion7(),
        RunId = Guid.CreateVersion7(),
        WorkspaceId = Guid.CreateVersion7(),
        UserId = _oleg.Id,
        ModelId = _seed.Id,
        TokensIn = 1000,
        Cost = cost,
        CreatedUtc = Now
    };

    /// <summary>Стартовая строка справочника с подменёнными полями — с версией из базы, как с экрана</summary>
    private async Task<AdminModelEditDto> Edit(ApplicationContext db, decimal costIn = 0.3m, bool enabled = true) => new()
    {
        Provider = _seed.Provider,
        Key = _seed.Key,
        DisplayName = _seed.DisplayName,
        CostIn = costIn,
        CostCached = 0.075m,
        CostOut = 0.5m,
        IsEnabled = enabled,
        Version = await db.AiModels.AsNoTracking().Where(x => x.Id == _seed.Id).Select(x => x.Version).SingleOrDefaultAsync()
    };

    /// <summary>Своя карта: одна цифра во всех полях</summary>
    private static AdminModelEditDto Local() => new()
    {
        Provider = "local",
        Key = "qwen3-30b",
        DisplayName = "Локальная 3090",
        CostIn = 0.05m,
        CostCached = 0.05m,
        CostOut = 0.05m,
        IsEnabled = true,
        IsLocal = true
    };
}
