using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PetTemplate.Api.Extensions;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Assistant.Transcription;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto.Assistant;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;
using PetTemplate.Shared.Options;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// Ассистент (спец. assistant, раздел 14).
///
/// <c>[AuthorizePermissions]</c> <b>без</b> прав — «участник пространства» (спец.
/// authorization, 5): чат — личный экран человека, и своего права у него нет. Набор
/// прав тут поставить нельзя — атрибут работает как «любое из», и обладатель любого
/// права раздела получил бы доступ так же. Тред — пара «человек + пространство»,
/// поэтому чужой переписки отсюда не видно никому; доступ к данным разделов проверяют
/// сами инструменты (раздел 9).
///
/// Эндпоинтов «подтвердить» и «отклонить» здесь нет: согласие — обычное сообщение,
/// а применение плана живёт внутри хода (5.1). Здесь же память о человеке
/// (<c>Memory</c>, 12.5) и голос (<c>Transcribe</c>, раздел 6): оба — про тот же тред
/// «человек + пространство», и своего права у них нет.
/// </summary>
[Route("api/[controller]/[action]")]
[Authorize]
[AuthorizePermissions]
[ValidateRequest]
public class AssistantController(
    AssistantThreadService threads,
    MemoryService memory,
    VoiceInputService voice,
    RunNotifier notifier,
    IOptions<AssistantOptions> options) : ControllerBase
{
    /// <summary>
    /// Подстраховка долгого опроса: раз в две секунды перечитываем базу на случай,
    /// когда ход идёт в другом инстансе и сигнал до нас не дойдёт
    /// </summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Лента треда: последние сообщения, живой ход и курсор на страницу выше.
    /// Грузится как в мессенджере — кнопки «показать ещё» нет
    /// </summary>
    [HttpGet]
    [Produces(typeof(AssistantHistoryDto))]
    public async Task<IActionResult> History([FromQuery] Guid? cursor, CancellationToken ct)
    {
        return Ok(await threads.History(User.GetUserId(), HttpContext.GetWorkspaceId(), cursor, ct));
    }

    /// <summary>
    /// Сообщение человека. Ход возвращается всегда — и для одношаговой просьбы тоже
    /// (раздел 3). Если ассистент ещё отвечает на прошлое, сообщение встанет
    /// в очередь треда, а в ответе приедет идущий ход
    /// </summary>
    [HttpPost]
    [Produces(typeof(AssistantSendResultDto))]
    public async Task<IActionResult> Send([FromBody] AssistantSendDto dto, CancellationToken ct)
    {
        var (runId, messageId) = await threads.Send(
            User.GetUserId(), HttpContext.GetWorkspaceId(), dto.Text, HttpContext.GetTimeZone(), ct);

        return Ok(new AssistantSendResultDto { RunId = runId, MessageId = messageId });
    }

    /// <summary>
    /// Состояние хода. С <paramref name="wait"/> — долгий опрос: сервер держит запрос
    /// до смены состояния или до истечения секунд. Это единственный способ узнать,
    /// что ход закончился: уведомлений в продукте нет
    /// </summary>
    [HttpGet("{id:guid}")]
    [Produces(typeof(AssistantRunDto))]
    public async Task<IActionResult> Run(Guid id, [FromQuery] int wait, CancellationToken ct)
    {
        var workspaceId = HttpContext.GetWorkspaceId();
        var userId = User.GetUserId();
        var run = await threads.RunState(id, userId, workspaceId, ct);
        if (run == null) return NotFound();

        if (wait <= 0 || run.Status != AssistantRunStatus.Running) return Ok(run);

        var deadline = DateTime.UtcNow.AddSeconds(Math.Min(wait, options.Value.LongPollSeconds));
        var seen = (run.Status, run.Step, run.Escalated);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            // Сигнал внутри процесса — быстрота, перечитывание базы — гарантия:
            // ход может идти в другом инстансе API (раздел 14)
            var slice = deadline - DateTime.UtcNow;
            await notifier.Wait(id, slice < Heartbeat ? slice : Heartbeat, ct);

            run = await threads.RunState(id, userId, workspaceId, ct);
            if (run == null) return NotFound();

            if ((run.Status, run.Step, run.Escalated) != seen) break;
        }

        return Ok(run);
    }

    /// <summary>
    /// «Остановить». Сделанное по уже сказанному «да» остаётся, несогласованный план
    /// становится черновиком, а в ленту приходит сообщение с тем, что успел (раздел 3)
    /// </summary>
    // Путь задан целиком: раздел 14 называет его Run/{id}/Stop, и клиент собирает
    // его из того же идентификатора хода, что и долгий опрос
    [HttpPost("~/api/Assistant/Run/{id:guid}/Stop")]
    public async Task<IActionResult> Stop(Guid id, CancellationToken ct)
    {
        return await threads.Stop(id, User.GetUserId(), HttpContext.GetWorkspaceId(), ct) ? Ok() : NotFound();
    }

    /// <summary>
    /// «Показать ход мысли» (13.2): шаги хода в читаемом виде. Трассу старше срока
    /// хранения уже вычистили — тогда приедет флаг, а не пустой список
    /// </summary>
    [HttpGet("~/api/Assistant/Run/{id:guid}/Trace")]
    [Produces(typeof(AssistantTraceDto))]
    public async Task<IActionResult> Trace(Guid id, CancellationToken ct)
    {
        var trace = await threads.Trace(id, User.GetUserId(), HttpContext.GetWorkspaceId(), ct);

        return trace == null ? NotFound() : Ok(trace);
    }

    /// <summary>
    /// «Очистить»: стирает переписку и трассы. Прогоны и планы остаются (12.1.1)
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await threads.Clear(User.GetUserId(), HttpContext.GetWorkspaceId(), ct);

        return Ok();
    }

    /// <summary>
    /// Голос → текст (6.1). Расшифровка возвращает текст в поле ввода, а не отправляет
    /// его: транскрипция ошибается в именах и числах, а уходить в тред должно то,
    /// что человек видел глазами.
    ///
    /// Аудио никуда не сохраняется — живёт в памяти ровно до конца расшифровки (раздел 9):
    /// сервер меряет по нему длительность и отдаёт движку. Лимит пространства проверяется до
    /// обращения к поставщику, как у <c>Send</c>: расшифровка идёт в тот же лимит (13.6)
    /// </summary>
    /// <param name="audio">Запись как есть: контейнер и кодек — какие умеет браузер (6.3)</param>
    /// <param name="seconds">Длительность, которую намерил клиент, — для учёта, если контейнер не узнан</param>
    [HttpPost]
    [AudioSizeLimit]
    [Produces(typeof(AssistantTranscriptionDto))]
    public async Task<IActionResult> Transcribe(IFormFile? audio, [FromForm] int? seconds, CancellationToken ct)
    {
        if (audio == null) throw new BadRequestException(AssistantErrors.TranscriptionFailed);

        await using var stream = audio.OpenReadStream();
        var text = await voice.Transcribe(
            User.GetUserId(),
            HttpContext.GetWorkspaceId(),
            stream,
            audio.Length,
            audio.FileName,
            audio.ContentType,
            seconds,
            ct);

        return Ok(new AssistantTranscriptionDto { Text = text });
    }

    /// <summary>
    /// Факты памяти: общие для пространства и личные (12.5). Память обязана быть видимой —
    /// это одновременно ответ про приватность и про корректность
    /// </summary>
    [HttpGet]
    [Produces(typeof(AssistantMemoryDto))]
    public async Task<IActionResult> Memory(CancellationToken ct)
    {
        var facts = await memory.List(User.GetUserId(), HttpContext.GetWorkspaceId(), ct);

        return Ok(new AssistantMemoryDto
        {
            Shared = [.. facts.Where(x => x.UserId == null).Select(MemoryMapping.ToDto)],
            Personal = [.. facts.Where(x => x.UserId != null).Select(MemoryMapping.ToDto)]
        });
    }

    /// <summary>
    /// Убрать факт — насовсем и в один нажим. Работает сколько угодно времени спустя;
    /// сообщение в ленте остаётся, а строка «Запомнил» под ним пропадает вместе с фактом (12.5).
    /// Чужой личный факт и факт чужого пространства — <c>404</c>, как несуществующий
    /// </summary>
    [HttpDelete("~/api/Assistant/Memory/{id:guid}")]
    public async Task<IActionResult> Forget(Guid id, CancellationToken ct)
    {
        return await memory.Delete(id, User.GetUserId(), HttpContext.GetWorkspaceId(), ct) ? Ok() : NotFound();
    }
}
