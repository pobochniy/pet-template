using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PetTemplate.Services.Assistant;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Assistant;

/// <summary>
/// Агентный цикл (спец. assistant, разделы 3 и 8). Модель заменена заранее записанными
/// ответами: в сеть тут не ходит никто, а «модель позвала инструмент и остановилась»
/// значит ровно это. Пишущие инструменты — тестового раздела (<c>TestPlan</c>)
/// </summary>
public class AgentLoopTests
{
    /// <summary>
    /// Одна просьба — один шаг: инструмент сложил дело в план, модель написала
    /// прозу, в базе появился план и ни одной записи раздела (5.1)
    /// </summary>
    [Fact]
    public async Task SimpleRunEndsWithPendingPlan()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пончики и Плюшки");
        var run = world.StartRun();
        await world.Save();
        var chat = new FakeChatClient()
            .Calling("create_test_note", new { title = "Купить бублики" })
            .Says("Записал в план. Делаем?");

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(AssistantRunStatus.Done, run.Status);
        Assert.Equal(AssistantStopReason.Completed, run.StopReason);
        Assert.Equal(1, run.Step);
        Assert.False(run.Escalated);
        Assert.Null(run.CurrentActivity);
        Assert.Equal(world.Model.Id, run.AiModelId);
        Assert.Empty(world.Notes.Notes);

        var plan = await world.Db.AssistantPlans.SingleAsync(CancellationToken.None);
        Assert.Equal(AssistantPlanStatus.Pending, plan.Status);
        Assert.Contains("Купить бублики", plan.Operations);

        var message = await world.Db.AssistantMessages.SingleAsync(CancellationToken.None);
        Assert.Equal("Записал в план. Делаем?", message.Content);
        Assert.Equal(plan.Id, message.PlanId);
        Assert.Equal(run.Id, message.RunId);
    }

    /// <summary>
    /// Ход упёрся в малый потолок, а модель всё ещё зовёт инструменты — бюджет
    /// поднимается сам, и это видно в ленте плашкой (раздел 3)
    /// </summary>
    [Fact]
    public async Task LongRunEscalates()
    {
        // Arrange
        var world = await AssistantWorld.Build("Шарики и Палочки");
        world.Settings.StepsSoft = 3;
        world.Settings.StepsHard = 20;
        var run = world.StartRun();
        await world.Save();

        var chat = new FakeChatClient();
        for (var i = 0; i < 5; i++) chat.Calling("search_help", new { query = $"вопрос {i}" });
        chat.Says("В справке этого нет.");

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.True(run.Escalated);
        Assert.Equal(5, run.Step);
        Assert.Equal(AssistantRunStatus.Done, run.Status);
    }

    /// <summary>
    /// Упёршийся предохранитель виден человеку текстом, а не пятисоткой и не пустотой
    /// (раздел 8). Потолок здесь низкий нарочно: проверяется правило, а не число
    /// </summary>
    [Fact]
    public async Task StepLimitBreaksRunWithText()
    {
        // Arrange
        var world = await AssistantWorld.Build("Бублики и Сушки");
        world.Settings.StepsSoft = 2;
        world.Settings.StepsHard = 3;
        var run = world.StartRun();
        await world.Save();

        var chat = new FakeChatClient();
        for (var i = 0; i < 10; i++) chat.Calling("search_help", new { query = $"вопрос {i}" });

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(AssistantStopReason.StepLimit, run.StopReason);
        Assert.Equal(3, run.Step);
        var message = await world.Db.AssistantMessages.SingleAsync(CancellationToken.None);
        Assert.Equal(AssistantTexts.StepLimitWithNothing, message.Content);
    }

    /// <summary>
    /// Потолок токенов на ход — тот же обрыв с частичным результатом (раздел 8)
    /// </summary>
    [Fact]
    public async Task TokenLimitBreaksRunWithText()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пряники и Вафли");
        world.Settings.TokensPerRun = 1000;
        var run = world.StartRun();
        await world.Save();
        var chat = new FakeChatClient()
            .Calling("search_help", new { query = "как пригласить" }).Costing(900, 200)
            .Says("не дойдёт");

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(AssistantStopReason.TokenLimit, run.StopReason);
        Assert.Equal(900, run.TokensIn);
        Assert.Equal(200, run.TokensOut);
        var message = await world.Db.AssistantMessages.SingleAsync(CancellationToken.None);
        Assert.StartsWith(AssistantTexts.TokenLimit, message.Content);
    }

    /// <summary>
    /// Модель узнаёт, что бюджет кончается, до того, как ход оборвётся: иначе доля
    /// обрывов наполовину состоит из ходов, которым не хватило шага на сводку (раздел 8)
    /// </summary>
    [Fact]
    public async Task ModelIsWarnedBeforeBudgetEnds()
    {
        // Arrange
        var world = await AssistantWorld.Build("Крылышки и Ножки");
        world.Settings.StepsSoft = 2;
        world.Settings.StepsHard = 4;
        var run = world.StartRun();
        await world.Save();

        var chat = new FakeChatClient();
        for (var i = 0; i < 6; i++) chat.Calling("search_help", new { query = $"вопрос {i}" });

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Contains(ToolResults(chat), x => x.Contains("осталось"));
    }

    /// <summary>
    /// Третий одинаковый вызов — предупреждение, пятый — обрыв: ловится хешем
    /// аргументов, без модели (раздел 8)
    /// </summary>
    [Fact]
    public async Task RepeatedCallStopsTheRun()
    {
        // Arrange
        var world = await AssistantWorld.Build("Винтики и Шпунтики");
        world.Settings.StepsHard = 20;
        var run = world.StartRun();
        await world.Save();
        var chat = new FakeChatClient().CallingTimes(8, "search_help", new { query = "одно и то же" });

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(AssistantStopReason.StepLimit, run.StopReason);
        Assert.Equal(5, run.Step);
        // Пятый результат модель уже не читает: на нём ход и кончился
        var results = ToolResults(chat);
        Assert.Equal(4, results.Count);
        Assert.DoesNotContain("повторяешь", results[1]);
        Assert.Contains("повторяешь тот же вызов", results[2]);
    }

    /// <summary>
    /// Модель придумала инструмент — это разговор, а не падение: ей отвечают
    /// строкой, и ход идёт дальше (раздел 7)
    /// </summary>
    [Fact]
    public async Task UnknownToolIsAnsweredNotThrown()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ложки и Поварёшки");
        var run = world.StartRun();
        await world.Save();
        var chat = new FakeChatClient()
            .Calling("delete_everything")
            .Says("Так не умею.");

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(AssistantRunStatus.Done, run.Status);
        Assert.Contains(ToolResults(chat), x => x.Contains("инструмента «delete_everything» нет"));
    }

    /// <summary>
    /// «Остановить» доходит до обработчика через статус прогона: он перечитывается
    /// из базы перед каждым обращением к модели, и это переживает несколько инстансов
    /// API (раздел 3). Собранная часть плана остаётся черновиком — без вопроса «делаем?»
    /// </summary>
    [Fact]
    public async Task StopBetweenStepsEndsRunWithWhatItManaged()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ложки и Поварёшки");
        var run = world.StartRun();
        await world.Save();

        // Первый инструмент отработать успевает, второй — уже нет: «Остановить»
        // пришло, пока модель думала над вторым шагом
        var chat = new FakeChatClient()
            .Calling("create_test_note", new { title = "Купить краску" })
            .Calling("search_help", new { query = "как красить" })
            .Says("этого ответа уже не будет");
        chat.OnAsk = number =>
        {
            if (number != 2) return;

            run.Status = AssistantRunStatus.Stopped;
            world.Db.SaveChanges();
        };

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(AssistantRunStatus.Stopped, run.Status);
        Assert.Equal(AssistantStopReason.UserStopped, run.StopReason);
        Assert.Equal(2, chat.Calls);
        Assert.Equal(1, run.Step);

        var message = await world.Db.AssistantMessages.SingleAsync(CancellationToken.None);
        Assert.Contains("Остановил", message.Content);
        // Собранная часть плана показывается без вопроса «делаем?»: человек прервал сам
        Assert.Contains("Черновик", message.Content);
        var plan = await world.Db.AssistantPlans.SingleAsync(CancellationToken.None);
        Assert.Equal(AssistantPlanStatus.Draft, plan.Status);
    }

    /// <summary>
    /// «Остановить» пришло, пока модель писала финальный ответ: ответ в ленте есть,
    /// но ход — Stopped, а не Done поверх остановки, и план — черновик (раздел 3)
    /// </summary>
    [Fact]
    public async Task StopDuringFinalAnswerKeepsRunStopped()
    {
        // Arrange
        var world = await AssistantWorld.Build("Вилки и Ножи");
        var run = world.StartRun();
        await world.Save();

        var chat = new FakeChatClient()
            .Calling("create_test_note", new { title = "Купить краску" })
            .Says("Записал заметку. Делаем?");
        chat.OnAsk = number =>
        {
            if (number != 2) return;

            run.Status = AssistantRunStatus.Stopped;
            world.Db.SaveChanges();
        };

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(AssistantRunStatus.Stopped, run.Status);
        Assert.Equal(AssistantStopReason.UserStopped, run.StopReason);

        var message = await world.Db.AssistantMessages.SingleAsync(CancellationToken.None);
        Assert.Contains("Остановил", message.Content);
        Assert.Contains("Записал заметку", message.Content);
        var plan = await world.Db.AssistantPlans.SingleAsync(CancellationToken.None);
        Assert.Equal(AssistantPlanStatus.Draft, plan.Status);
    }

    /// <summary>
    /// Вопрос завершает ход: после него инструменты не исполняются, а собранная часть
    /// плана сохраняется черновиком (5.2)
    /// </summary>
    [Fact]
    public async Task AskUserEndsRunAndSavesDraft()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ватрушки и Пышки");
        var run = world.StartRun();
        await world.Save();
        // Вопрос и ещё один вызов — одним ответом модели: второй уже не исполняется
        var chat = new FakeChatClient()
            .Calling("create_test_note", new { title = "Забронировать домик" })
            .Responds(string.Empty,
            [
                ("ask_user", new Dictionary<string, object?> { ["question"] = "На сколько дней?", ["options"] = "на два|на три" }),
                ("create_test_note", new Dictionary<string, object?> { ["title"] = "этого вызова не будет" })
            ])
            .Says("Уточните, пожалуйста.");

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        var message = await world.Db.AssistantMessages.SingleAsync(CancellationToken.None);
        Assert.Equal("Уточните, пожалуйста.", message.Content);
        Assert.Equal(["на два", "на три"], JsonSerializer.Deserialize<string[]>(message.Options!)!);

        var plan = await world.Db.AssistantPlans.SingleAsync(CancellationToken.None);
        Assert.Equal(AssistantPlanStatus.Draft, plan.Status);
        Assert.DoesNotContain("этого вызова не будет", plan.Operations);
        Assert.Equal(plan.Id, message.PlanId);
        Assert.Equal(AssistantRunStatus.Done, run.Status);
    }

    /// <summary>
    /// Модель задала вопрос и промолчала в последней реплике — в ленту идёт сам вопрос
    /// из <c>ask_user</c>: варианты ответа под «модель не отозвалась» были бы бессмыслицей (5.2)
    /// </summary>
    [Fact]
    public async Task AskUserWithoutReplyShowsTheQuestion()
    {
        // Arrange
        var world = await AssistantWorld.Build("Ватрушки и Пышки");
        var run = world.StartRun();
        await world.Save();
        var chat = new FakeChatClient()
            .Responds(string.Empty,
            [
                ("ask_user", new Dictionary<string, object?> { ["question"] = "На сколько дней?", ["options"] = "на два|на три" })
            ])
            .Says(string.Empty);

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        var message = await world.Db.AssistantMessages.SingleAsync(CancellationToken.None);
        Assert.Equal("На сколько дней?", message.Content);
        Assert.Equal(["на два", "на три"], JsonSerializer.Deserialize<string[]>(message.Options!)!);
    }

    /// <summary>
    /// Незакрытый план в треде один: новая просьба поверх висящего плана его отменяет (5.4)
    /// </summary>
    [Fact]
    public async Task NewPlanCancelsPrevious()
    {
        // Arrange
        var world = await AssistantWorld.Build("Кастрюли и Сковородки");
        var old = world.Plan(AssistantWorld.CreateNote("Старое дело"));
        var run = world.StartRun();
        await world.Save();
        var chat = new FakeChatClient()
            .Calling("create_test_note", new { title = "Новое дело" })
            .Says("Делаем?");

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        var stored = await world.Db.AssistantPlans.AsNoTracking()
            .SingleAsync(x => x.Id == old.Id, CancellationToken.None);
        Assert.Equal(AssistantPlanStatus.Cancelled, stored.Status);
        Assert.Equal(1, await world.Db.AssistantPlans
            .CountAsync(x => x.Status == AssistantPlanStatus.Pending, CancellationToken.None));
    }

    /// <summary>
    /// Промпт и история и правда доезжают до модели: первым сообщением — системный
    /// промпт с пространством, дальше — переписка (раздел 7, 12.4)
    /// </summary>
    [Fact]
    public async Task ModelSeesPromptAndHistory()
    {
        // Arrange
        var world = await AssistantWorld.Build("Котики и Собачки");
        world.Message(AssistantMessageRole.User, "как пригласить коллегу?");
        var run = world.StartRun();
        await world.Save();
        var chat = new FakeChatClient().Says("По справке — так.");

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        var sent = chat.Asked.Single();
        Assert.Equal(ChatRole.System, sent[0].Role);
        Assert.Contains("Котики и Собачки", sent[1].Text);
        Assert.Equal("как пригласить коллегу?", sent[2].Text);
    }

    /// <summary>
    /// Пустой ответ модели — не пустое сообщение в ленте: человек получает строку,
    /// что модель не ответила (раздел 3)
    /// </summary>
    [Fact]
    public async Task EmptyAnswerIsNeverAnEmptyMessage()
    {
        // Arrange
        var world = await AssistantWorld.Build("Тапки и Носки");
        var run = world.StartRun();
        await world.Save();

        // Act
        await world.Runner(new FakeChatClient().Says("   ")).Execute(run.Id, null, CancellationToken.None);

        // Assert
        var message = await world.Db.AssistantMessages.SingleAsync(CancellationToken.None);
        Assert.Equal(AssistantTexts.ProviderError, message.Content);
    }

    /// <summary>Результаты инструментов, которые модель увидела к последнему обращению</summary>
    private static List<string> ToolResults(FakeChatClient chat) =>
        chat.Asked[^1]
            .SelectMany(x => x.Contents)
            .OfType<FunctionResultContent>()
            .Select(x => x.Result?.ToString() ?? string.Empty)
            .ToList();

    /// <summary>
    /// Автора убрали из пространства, пока ход ждал очереди: модель не зовётся, ход
    /// кончается строкой в ленте (раздел 9)
    /// </summary>
    [Fact]
    public async Task RemovedMemberRunDoesNotAskModel()
    {
        // Arrange
        var world = await AssistantWorld.Build("Рога и Копыта");
        world.Message(AssistantMessageRole.User, "найди заметки");
        var run = world.StartRun();
        await world.Save();
        await world.Db.UserInPermissions.Where(x => x.UserId == world.Author)
            .ExecuteDeleteAsync(CancellationToken.None);
        var chat = new FakeChatClient().Says("Нашёл.");

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        Assert.Equal(0, chat.Calls);
        var finished = await world.NewContext().AssistantRuns.SingleAsync(x => x.Id == run.Id, CancellationToken.None);
        Assert.Equal(AssistantRunStatus.Done, finished.Status);
        var answer = await world.NewContext().AssistantMessages
            .SingleAsync(x => x.RunId == run.Id && x.Role == AssistantMessageRole.Assistant, CancellationToken.None);
        Assert.Equal(AssistantTexts.NotAMember, answer.Content);
    }
}
