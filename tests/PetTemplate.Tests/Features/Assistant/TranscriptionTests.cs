using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PetTemplate.Api.Configs;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant.Transcription;
using PetTemplate.Services.Billing;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto.Assistant;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Assistant;

/// <summary>
/// Голос (спец. assistant, раздел 6). Расшифровка возвращает текст в поле ввода, а не
/// отправляет его: отправлять надо то, что человек видел глазами. Движок — фейк
/// (<see cref="FakeTranscription"/>) или фейковый провод (<see cref="FakeTranscriptionHandler"/>):
/// в сеть ни один тест не ходит.
/// </summary>
public class TranscriptionTests
{
    private readonly PetTemplate.Db.Entity.Workspace _workspace;
    private readonly PetTemplate.Db.Entity.Profile _oleg;
    private readonly string _token;

    public TranscriptionTests()
    {
        (_workspace, _token, _oleg) = Given.Owner("Рога и Копыта");
    }

    /// <summary>
    /// Модель расшифровки не настроена — внятный отказ с кодом, а не тишина и не пятисотка:
    /// человек должен понять, что набрать придётся руками (6.3)
    /// </summary>
    [Fact]
    public async Task WithoutEngineAnswersWithCode()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пончики и Плюшки");
        using var content = Audio(seconds: 3);

        // Act
        var response = await world.Client.PostAsync("/api/Assistant/Transcribe", content, CancellationToken.None);

        // Assert
        await response.ShouldBeBadRequestWith(AssistantErrors.TranscriptionUnavailable);
    }

    /// <summary>
    /// Запрос целиком, по проводу: в движок того же поставщика, что у моделей, уезжают
    /// модель, явный язык (6.1) и логины участников подсказкой (6.2), ключ — заголовком,
    /// а текст возвращается в ответе
    /// </summary>
    [Fact]
    public async Task EngineGetsModelLanguageAndMemberNames()
    {
        // Arrange
        var engine = new FakeTranscriptionHandler("""{"text":" как пригласить Васю? "}""");
        var (client, _) = await Client(engine);
        using var content = Audio(seconds: 3);

        // Act
        var response = await client.PostAsync("/api/Assistant/Transcribe", content, CancellationToken.None);

        // Assert
        await response.ShouldBeSuccessful();
        var result = await response.Content.ReadFromJsonAsync<AssistantTranscriptionDto>(CancellationToken.None);
        Assert.Equal("как пригласить Васю?", result!.Text);
        Assert.Equal("http://voice.test/v1/audio/transcriptions", engine.Address!.ToString());
        Assert.Equal("Bearer voice-key", engine.Authorization);
        Assert.Equal("whisper-test", engine.Fields["model"]);
        Assert.Equal("ru", engine.Fields["language"]);
        Assert.Equal("Oleg", engine.Fields["prompt"]);
    }

    /// <summary>
    /// Расход голоса — строка служебного расхода с пространством и человеком (13.5),
    /// цена — секунды движка по цене минуты
    /// </summary>
    [Fact]
    public async Task TranscriptionIsChargedToWorkspaceAndPerson()
    {
        // Arrange
        var engine = new FakeTranscriptionHandler("""{"text":"привет","usage":{"type":"duration","seconds":30}}""");
        var (client, db) = await Client(engine);
        using var content = Audio(seconds: 29);

        // Act
        var response = await client.PostAsync("/api/Assistant/Transcribe", content, CancellationToken.None);

        // Assert
        await response.ShouldBeSuccessful();
        var spend = await db.ServiceSpend.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal(ServiceSpendKind.Transcription, spend.Kind);
        Assert.Equal(_workspace.Id, spend.WorkspaceId);
        Assert.Equal(_oleg.Id, spend.UserId);
        Assert.Equal("main", spend.Provider);
        Assert.Equal("whisper-test", spend.ModelKey);
        Assert.Null(spend.ModelId);
        Assert.Equal(30, spend.Seconds);
        Assert.Equal(1m, spend.Cost);
    }

    /// <summary>
    /// Ни движок, ни контейнер длительность не назвали — считаем по словам клиента:
    /// вызов был и стоил денег
    /// </summary>
    [Fact]
    public async Task ClientSecondsAreUsedWhenEngineIsSilent()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ёжики в Тумане");
        world.Settings.Transcription.Model = "whisper-test";
        world.Settings.Transcription.CostPerMinute = 6m;

        // Act
        await world.Voice(new FakeTranscription("привет"))
            .Transcribe(world.Author, world.WorkspaceId, new MemoryStream([1]), 1, "voice.webm", "audio/webm", 10,
                CancellationToken.None);

        // Assert
        var spend = await world.NewContext().ServiceSpend.SingleAsync(CancellationToken.None);
        Assert.Equal(10, spend.Seconds);
        Assert.Equal(1m, spend.Cost);
    }

    /// <summary>
    /// Лимит пространства проверяется до обращения к движку (13.6): расшифровка — тоже
    /// расход пространства, и отказ тот же, что у <c>Send</c>
    /// </summary>
    [Fact]
    public async Task LimitIsCheckedBeforeEngine()
    {
        // Arrange
        var world = await AssistantWorld.Build("Бублики и Сушки");
        world.SpendSettings.DefaultLimit.TokensPerDay = 1000;
        world.Db.RunSpend.Add(new RunSpend
        {
            Id = Guid.CreateVersion7(),
            RunId = Guid.CreateVersion7(),
            WorkspaceId = world.WorkspaceId,
            UserId = world.Author,
            ModelId = world.Model.Id,
            TokensIn = 1000,
            CreatedUtc = world.Now
        });
        await world.Save();
        var engine = new FakeTranscription("привет");

        // Act
        var refused = await Assert.ThrowsAsync<BadRequestException>(() => world.Voice(engine)
            .Transcribe(world.Author, world.WorkspaceId, new MemoryStream([1]), 1, "voice.webm", "audio/webm", 3,
                CancellationToken.None));

        // Assert
        Assert.Equal(AssistantErrors.DailyLimitReached.Code, refused.Code);
        Assert.Equal(0, engine.Calls);
    }

    /// <summary>
    /// Голос идёт в тот же лимит, что ходы: надиктованная минута съела дневной потолок
    /// денег — следующий <c>Send</c> получает отказ
    /// </summary>
    [Fact]
    public async Task TranscriptionCountsTowardWorkspaceLimit()
    {
        // Arrange
        var world = await AssistantWorld.Build("Шарики и Палочки");
        world.Settings.Transcription.CostPerMinute = 2m;
        world.SpendSettings.DefaultLimit.CostPerDay = 2m;
        await world.Voice(new FakeTranscription(new TranscriptionResult("привет", Seconds: 60)))
            .Transcribe(world.Author, world.WorkspaceId, new MemoryStream([1]), 1, "voice.webm", "audio/webm", 60,
                CancellationToken.None);

        // Act
        var refused = await Assert.ThrowsAsync<BadRequestException>(() =>
            world.Threads.Send(world.Author, world.WorkspaceId, "как пригласить?", "UTC", CancellationToken.None));

        // Assert
        Assert.Equal(AssistantErrors.DailyLimitReached.Code, refused.Code);
    }

    /// <summary>
    /// Компакция в лимит по-прежнему не идёт: её пространство не просило (13.6)
    /// </summary>
    [Fact]
    public async Task CompactionStillDoesNotCountTowardLimit()
    {
        // Arrange
        var world = await AssistantWorld.Build("Котики и Собачки");
        world.Db.ServiceSpend.Add(ServiceSpending.Of(ServiceSpendKind.Compaction, world.Model, 5000, 0, 5000,
            world.WorkspaceId));
        await world.Save();

        // Act
        var (tokens, _) = await new WorkspaceLimits(world.Db, Microsoft.Extensions.Options.Options.Create(world.SpendSettings),
                Microsoft.Extensions.Options.Options.Create(world.Settings))
            .Usage(world.WorkspaceId, WorkspaceLimits.DayStart(world.Now), CancellationToken.None);

        // Assert
        Assert.Equal(0, tokens);
    }

    /// <summary>Запись больше потолка — отказ кодом, движок не зовётся (6.1)</summary>
    [Fact]
    public async Task TooLargeAudioIsRefused()
    {
        // Arrange
        var world = await AssistantWorld.Build("Винтики и Шпунтики");
        world.Settings.Transcription.MaxAudioBytes = 2;
        var engine = new FakeTranscription("привет");

        // Act
        var refused = await Assert.ThrowsAsync<BadRequestException>(() => world.Voice(engine)
            .Transcribe(world.Author, world.WorkspaceId, new MemoryStream([1, 2, 3]), 3, "voice.webm", "audio/webm", 3,
                CancellationToken.None));

        // Assert
        Assert.Equal(AssistantErrors.AudioTooLarge.Code, refused.Code);
        Assert.Equal(0, engine.Calls);
    }

    /// <summary>Запись длиннее минуты — уже не реплика, а диктовка (6.3)</summary>
    [Fact]
    public async Task TooLongAudioIsRefused()
    {
        // Arrange
        var world = await AssistantWorld.Build("Крылышки и Ножки");
        var engine = new FakeTranscription("привет");

        // Act
        var refused = await Assert.ThrowsAsync<BadRequestException>(() => world.Voice(engine)
            .Transcribe(world.Author, world.WorkspaceId, new MemoryStream([1]), 1, "voice.webm", "audio/webm", 120,
                CancellationToken.None));

        // Assert
        Assert.Equal(AssistantErrors.AudioTooLong.Code, refused.Code);
        Assert.Equal(0, engine.Calls);
    }

    /// <summary>
    /// Потолок длительности держит сервер (6.1): запись в полторы минуты получает отказ
    /// по контейнеру, хотя клиент назвал три секунды
    /// </summary>
    [Fact]
    public async Task TooLongContainerIsRefusedWhateverClientSays()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пончики и Плюшки");
        var engine = new FakeTranscription("привет");
        var audio = AudioContainers.WebM(90);

        // Act
        var refused = await Assert.ThrowsAsync<BadRequestException>(() => world.Voice(engine)
            .Transcribe(world.Author, world.WorkspaceId, new MemoryStream(audio), audio.Length, "voice.webm",
                "audio/webm", 3, CancellationToken.None));

        // Assert
        Assert.Equal(AssistantErrors.AudioTooLong.Code, refused.Code);
        Assert.Equal(0, engine.Calls);
    }

    /// <summary>
    /// Движок длительность не назвал — учёт идёт по контейнеру, а не по занижённым
    /// секундам клиента (13.5)
    /// </summary>
    [Fact]
    public async Task ContainerSecondsBeatClientSeconds()
    {
        // Arrange
        var world = await AssistantWorld.Build("Шарики и Палочки");
        world.Settings.Transcription.Model = "whisper-test";
        world.Settings.Transcription.CostPerMinute = 6m;
        var audio = AudioContainers.FragmentedMp4(30);

        // Act
        await world.Voice(new FakeTranscription("привет"))
            .Transcribe(world.Author, world.WorkspaceId, new MemoryStream(audio), audio.Length, "voice.mp4",
                "audio/mp4", 1, CancellationToken.None);

        // Assert
        var spend = await world.NewContext().ServiceSpend.SingleAsync(CancellationToken.None);
        Assert.Equal(31, spend.Seconds);
    }

    /// <summary>
    /// Движок ответил не тем — отказ с кодом, который клиент покажет по-русски,
    /// а не пятисотка
    /// </summary>
    [Fact]
    public async Task BrokenEngineAnswerIsRefusal()
    {
        // Arrange
        var service = Service(new FakeTranscriptionHandler("не json"));

        // Act
        var refused = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.Transcribe(new MemoryStream([1]), "voice.webm", "audio/webm", "ru", null, CancellationToken.None));

        // Assert
        Assert.Equal(AssistantErrors.TranscriptionFailed.Code, refused.Code);
    }

    /// <summary>Движок лежит — тот же разговор, а не пятисотка</summary>
    [Fact]
    public async Task EngineDownIsRefusal()
    {
        // Arrange
        var service = Service(new FakeTranscriptionHandler("{}", HttpStatusCode.ServiceUnavailable));

        // Act
        var refused = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.Transcribe(new MemoryStream([1]), "voice.webm", "audio/webm", "ru", null, CancellationToken.None));

        // Assert
        Assert.Equal(AssistantErrors.TranscriptionFailed.Code, refused.Code);
    }

    /// <summary>
    /// Движок, считающий аудио токенами (<c>gpt-4o-transcribe</c>), отдаёт расход токенами —
    /// они и уходят в строку расхода
    /// </summary>
    [Fact]
    public async Task TokenUsageIsRead()
    {
        // Arrange
        var service = Service(new FakeTranscriptionHandler(
            """{"text":"да","usage":{"type":"tokens","input_tokens":120,"output_tokens":3}}"""));

        // Act
        var result = await service.Transcribe(new MemoryStream([1]), "voice.webm", "audio/webm", "ru", null,
            CancellationToken.None);

        // Assert
        Assert.Equal(new TranscriptionResult("да", null, 120, 3), result);
    }

    /// <summary>
    /// Приложение с настроенной расшифровкой: поставщик <c>main</c> с адресом и ключом,
    /// модель распознавания и цена минуты; провод — фейковый
    /// </summary>
    private Task<(HttpClient client, ApplicationContext db)> Client(FakeTranscriptionHandler engine) =>
        Given.ApiClient(x =>
            {
                x.Workspaces.Add(_workspace);
                x.Profiles.Add(_oleg);
            },
            _token,
            _workspace.Id,
            settings: new Dictionary<string, string?>
            {
                ["AssistantOptions:Providers:main:Endpoint"] = "http://voice.test/v1",
                ["AssistantOptions:Providers:main:ApiKey"] = "voice-key",
                ["AssistantOptions:Transcription:Model"] = "whisper-test",
                ["AssistantOptions:Transcription:CostPerMinute"] = "2"
            },
            configureServices: services => services
                .AddHttpClient(ServicesExtensions.TranscriptionClient)
                .ConfigurePrimaryHttpMessageHandler(() => engine));

    private static OpenAiCompatibleTranscriptionService Service(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("http://voice.test/v1/") },
        "voice-key",
        "whisper-test",
        NullLogger<OpenAiCompatibleTranscriptionService>.Instance);

    private static MultipartFormDataContent Audio(int seconds)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        content.Add(file, "audio", "voice.webm");
        content.Add(new StringContent(seconds.ToString()), "seconds");

        return content;
    }
}
