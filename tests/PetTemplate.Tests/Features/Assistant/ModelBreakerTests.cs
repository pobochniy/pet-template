using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Assistant.Models;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto.Assistant;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Assistant;

/// <summary>
/// Справочник моделей и размыкатель (спец. assistant, 16.2): ход уходит первой
/// включённой и замкнутой модели; модель, которая перестала отвечать, размыкается
/// сама, а после паузы получает один пробный ход. Состояние — в строке справочника
/// </summary>
public class ModelBreakerTests
{
    /// <summary>Ход уходит первой по порядку, и модель записана в прогон</summary>
    [Fact]
    public async Task RunGoesToFirstByPriority()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пончики и Плюшки");
        world.Db.AiModels.Add(AssistantWorld.AiModel("Запасная", 2, world.Now));
        var run = world.StartRun();
        await world.Save();
        var clients = new FakeModelChatClients(new FakeChatClient().Says("Привет."));

        // Act
        await world.Runner(clients).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(world.Model.Id, run.AiModelId);
        Assert.Equal(world.Model.Id, Assert.Single(clients.Asked).Id);
    }

    /// <summary>Выключенная модель пропускается — ход идёт к следующей</summary>
    [Fact]
    public async Task DisabledModelIsSkipped()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ёжики в Тумане");
        var spare = AssistantWorld.AiModel("Запасная", 2, world.Now);
        world.Db.AiModels.Add(spare);
        var run = world.StartRun();
        await world.Save();
        await Update(world, world.Model.Id, x => x.SetProperty(m => m.IsEnabled, false));

        // Act
        await world.Runner(new FakeChatClient().Says("Привет.")).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(spare.Id, run.AiModelId);
    }

    /// <summary>Разомкнутая модель пропускается, пока идёт пауза</summary>
    [Fact]
    public async Task OpenModelIsSkipped()
    {
        // Arrange
        var world = await AssistantWorld.Build("Бублики и Сушки");
        var spare = AssistantWorld.AiModel("Запасная", 2, world.Now);
        world.Db.AiModels.Add(spare);
        var run = world.StartRun();
        await world.Save();
        await Open(world, world.Now.AddMinutes(1));

        // Act
        await world.Runner(new FakeChatClient().Says("Привет.")).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(spare.Id, run.AiModelId);
    }

    /// <summary>
    /// Ходить некуда — ход кончается строкой «недоступен» в ленте, к модели не обращаясь
    /// </summary>
    [Fact]
    public async Task AllOpenFailsRunWithText()
    {
        // Arrange
        var world = await AssistantWorld.Build("Шарики и Палочки");
        var run = world.StartRun();
        await world.Save();
        await Open(world, world.Now.AddMinutes(1));
        var chat = new FakeChatClient().Says("Привет.");

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(AssistantRunStatus.Failed, run.Status);
        Assert.Equal(0, chat.Calls);
        var answer = await world.Db.AssistantMessages.SingleAsync(x => x.RunId == run.Id, CancellationToken.None);
        Assert.Equal(AssistantTexts.ModelUnavailable, answer.Content);
    }

    /// <summary>
    /// Ходить некуда ещё на входе — <c>Send</c> отказывает кодом (спец. assistant, 14):
    /// ни реплики, ни хода. Раньше лимита: лимит исчерпан, но и модели нет — человеку
    /// говорят про модель
    /// </summary>
    [Fact]
    public async Task SendRefusesWhenNoModelIsAvailable()
    {
        // Arrange
        var world = await AssistantWorld.Build("Кексы и Маффины");
        world.SpendSettings.DefaultLimit.TokensPerDay = 0;
        await Open(world, world.Now.AddMinutes(1));

        // Act
        var refused = await Assert.ThrowsAsync<BadRequestException>(() =>
            world.Threads.Send(world.Author, world.WorkspaceId, "привет", "UTC", CancellationToken.None));

        // Assert
        Assert.Equal(AssistantErrors.ModelUnavailable.Code, refused.Code);
        var db = world.NewContext();
        Assert.False(await db.AssistantMessages.AnyAsync(CancellationToken.None));
        Assert.False(await db.AssistantRuns.AnyAsync(CancellationToken.None));
    }

    /// <summary>Выключенная модель — то же, что разомкнутая: отказ <c>400</c> с кодом словаря <c>assistant</c></summary>
    [Fact]
    public async Task SendAnswersModelUnavailableCode()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ириски и Тянучки");
        await Update(world, world.Model.Id, x => x.SetProperty(m => m.IsEnabled, false));

        // Act
        var response = await world.Client.PostAsJsonAsync("/api/Assistant/Send",
            new AssistantSendDto { Text = "привет" }, CancellationToken.None);

        // Assert
        await response.ShouldBeBadRequestWith(AssistantErrors.ModelUnavailable);
    }

    /// <summary>Модель, чья пауза вышла, ждёт пробы — на входе она доступна</summary>
    [Fact]
    public async Task SendAcceptsModelAwaitingProbe()
    {
        // Arrange
        var world = await AssistantWorld.Build("Леденцы и Карамельки");
        await Open(world, world.Now.AddSeconds(-1));

        // Act
        var (runId, _) = await world.Threads.Send(world.Author, world.WorkspaceId, "привет", "UTC",
            CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, runId);
    }

    /// <summary>Пустой ответ — плохой ответ, но не отказ поставщика: счётчик не растёт</summary>
    [Fact]
    public async Task EmptyAnswerIsNotFailure()
    {
        // Arrange
        var world = await AssistantWorld.Build("Котики и Собачки");
        var run = world.StartRun();
        await world.Save();

        // Act
        await world.Runner(new FakeChatClient().Says(string.Empty)).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(0, (await Stored(world)).FailureCount);
    }

    /// <summary>Три отказа подряд размыкают модель на первую паузу — минуту</summary>
    [Fact]
    public async Task ThreeFailuresOpenModel()
    {
        // Arrange
        var world = await AssistantWorld.Build("Крылышки и Ножки");
        await world.Save();
        var before = DateTime.UtcNow;

        // Act
        for (var i = 0; i < 3; i++)
        {
            var run = world.StartRun();
            await world.Save();
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                world.Runner(Failing()).Execute(run.Id, null, CancellationToken.None));
            await Finish(world, run);
        }

        // Assert
        var model = await Stored(world);
        Assert.Equal(3, model.FailureCount);
        Assert.Equal(60, model.OpenForSeconds);
        Assert.InRange(model.OpenUntilUtc!.Value, before.AddSeconds(59), DateTime.UtcNow.AddSeconds(61));
    }

    /// <summary>Ответ после отказов обнуляет счётчик: отказы считаются подряд</summary>
    [Fact]
    public async Task AnswerResetsFailureCount()
    {
        // Arrange
        var world = await AssistantWorld.Build("Винтики и Шпунтики");
        var run = world.StartRun();
        await world.Save();
        await Update(world, world.Model.Id, x => x.SetProperty(m => m.FailureCount, 2));

        // Act
        await world.Runner(new FakeChatClient().Says("Привет.")).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(0, (await Stored(world)).FailureCount);
    }

    /// <summary>Пробный ход после паузы удался — модель замкнута целиком</summary>
    [Fact]
    public async Task ProbeAfterPauseClosesModel()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ложки и Поварёшки");
        var run = world.StartRun();
        await world.Save();
        await Open(world, world.Now.AddSeconds(-1));

        // Act
        await world.Runner(new FakeChatClient().Says("Привет.")).Execute(run.Id, null, CancellationToken.None);

        // Assert
        var model = await Stored(world);
        Assert.Equal(world.Model.Id, run.AiModelId);
        Assert.Equal(0, model.FailureCount);
        Assert.Null(model.OpenUntilUtc);
        Assert.Equal(0, model.OpenForSeconds);
    }

    /// <summary>Пробный ход не прошёл — снова пауза, вдвое длиннее прежней</summary>
    [Fact]
    public async Task FailedProbeDoublesPause()
    {
        // Arrange
        var world = await AssistantWorld.Build("Вилки и Ножи");
        var run = world.StartRun();
        await world.Save();
        await Open(world, world.Now.AddSeconds(-1));
        var before = DateTime.UtcNow;

        // Act
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            world.Runner(Failing()).Execute(run.Id, null, CancellationToken.None));

        // Assert
        var model = await Stored(world);
        Assert.Equal(120, model.OpenForSeconds);
        Assert.InRange(model.OpenUntilUtc!.Value, before.AddSeconds(119), DateTime.UtcNow.AddSeconds(121));
    }

    /// <summary>Пауза вышла у двух экземпляров сразу — проба достаётся одному</summary>
    [Fact]
    public async Task OnlyOneInstanceGetsProbe()
    {
        // Arrange
        var world = await AssistantWorld.Build("Чашки и Блюдца");
        await world.Save();
        await Open(world, world.Now.AddSeconds(-1));
        await using var first = world.NewContext();
        await using var second = world.NewContext();

        // Act
        var picks = await Task.WhenAll(Catalog(world, first).Pick(CancellationToken.None),
            Catalog(world, second).Pick(CancellationToken.None));

        // Assert
        var probe = Assert.Single(picks, x => x != null);
        Assert.True(probe!.Probe);
        Assert.Single(picks, x => x == null);
    }

    /// <summary>
    /// Что считается отказом поставщика, а что — нашим запросом или кнопкой (16.2)
    /// </summary>
    [Theory]
    [MemberData(nameof(Failures))]
    public void ClassifiesFailures(Exception error, bool stopped, bool expected)
    {
        // Arrange
        using var source = new CancellationTokenSource();
        if (stopped) source.Cancel();

        // Act
        var failure = ModelBreaker.IsFailure(error, source.Token);

        // Assert
        Assert.Equal(expected, failure);
    }

    public static TheoryData<Exception, bool, bool> Failures() => new()
    {
        { new HttpRequestException("connection refused"), false, true },
        { new ClientResultException("no response", null, new HttpRequestException("reset")), false, true },
        { new ClientResultException(new StatusResponse(503)), false, true },
        { new ClientResultException(new StatusResponse(500)), false, true },
        { new ClientResultException(new StatusResponse(401)), false, true },
        { new ClientResultException(new StatusResponse(403)), false, true },
        { new ClientResultException(new StatusResponse(429)), false, true },
        { new ClientResultException(new StatusResponse(400)), false, false },
        { new ClientResultException(new StatusResponse(404)), false, false },
        { new TaskCanceledException("HttpClient.Timeout", new TimeoutException()), false, true },
        { new OperationCanceledException(), true, false },
        { new IOException("broken pipe", new SocketException()), false, true },
        { new InvalidOperationException("наша ошибка"), false, false }
    };

    private static FakeChatClient Failing() =>
        new() { OnAsk = _ => throw new HttpRequestException("connection refused") };

    private static AiModelCatalog Catalog(AssistantWorld world, ApplicationContext db) => new(
        db,
        new ModelBreaker(db, Microsoft.Extensions.Options.Options.Create(world.BreakerSettings),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ModelBreaker>.Instance),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AiModelCatalog>.Instance);

    private static Task<AiModel> Stored(AssistantWorld world) =>
        world.Db.AiModels.AsNoTracking().SingleAsync(x => x.Id == world.Model.Id, CancellationToken.None);

    private static Task Open(AssistantWorld world, DateTime until) =>
        Update(world, world.Model.Id, x => x
            .SetProperty(m => m.FailureCount, 3)
            .SetProperty(m => m.OpenForSeconds, 60)
            .SetProperty(m => m.OpenUntilUtc, until));

    private static Task Update(
        AssistantWorld world,
        Guid id,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<AiModel>> set) =>
        world.Db.AiModels.Where(x => x.Id == id).ExecuteUpdateAsync(set, CancellationToken.None);

    /// <summary>Упавший ход доводит до конца обработчик; здесь — руками, чтобы завести следующий</summary>
    private static async Task Finish(AssistantWorld world, AssistantRun run)
    {
        run.Status = AssistantRunStatus.Failed;
        await world.Save();
    }

    /// <summary>Ответ поставщика с одним только статусом — всё, что нужно классификации</summary>
    private sealed class StatusResponse(int status) : PipelineResponse
    {
        public override int Status => status;

        public override string ReasonPhrase => string.Empty;

        public override Stream? ContentStream { get; set; }

        public override BinaryData Content => BinaryData.Empty;

        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Content);

        public override void Dispose()
        {
        }
    }
}
