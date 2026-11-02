using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto.Admin;

namespace PetTemplate.Services.Assistant.Check;

/// <summary>
/// Что разобралось из фразы сверки (спец. admin, 15.3). Мерить надо не только попадание
/// по инструменту: язык ответа и отсутствие слов об устройстве приложения — то, по чему
/// видно, стало лучше или хуже. Поля Lad про задачи (тип дела, время, участник) в ядре
/// не меряются: инструментов, которые их пишут, в ядре нет
/// </summary>
public class ModelCheckOutcome
{
    /// <summary>Ответ в отчёте — для глаз</summary>
    private const int AnswerMaxLength = 300;

    public ModelCheckPhrase Phrase { get; set; } = new();

    public List<string> Tools { get; set; } = [];

    public string Answer { get; set; } = string.Empty;

    public string? Error { get; set; }

    /// <summary>Все входящие, как их отдаёт поставщик: кешированные — внутри</summary>
    public long TokensIn { get; set; }

    public long TokensCached { get; set; }

    public long TokensOut { get; set; }

    public int Steps { get; set; }

    public List<RecordedResponse> Responses { get; set; } = [];

    public bool ToolHit => Phrase.Tool == null || Tools.Contains(Phrase.Tool);

    public bool CleanAnswer => Phrase.Forbidden == null
                               || !Phrase.Forbidden.Any(x => Answer.Contains(x, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ответ на языке профиля — отдельный пункт набора: модель, отвечающая по-английски
    /// на русскую просьбу, непригодна независимо от разбора
    /// </summary>
    public bool RussianAnswer => Answer.Any(x => x is >= 'а' and <= 'я' or >= 'А' and <= 'Я');

    public static ModelCheckOutcome From(
        ModelCheckPhrase phrase,
        RecordingChatClient recorder,
        string answer,
        AssistantRun run)
    {
        var outcome = new ModelCheckOutcome
        {
            Phrase = phrase,
            Answer = answer,
            Tools = Called(recorder),
            Steps = run.Step,
            Responses = [.. recorder.Responses]
        };
        outcome.CountTokens(recorder);

        return outcome;
    }

    /// <summary>Сорвавшийся ход тоже стоил тех токенов, что успел получить</summary>
    public static ModelCheckOutcome Failed(ModelCheckPhrase phrase, RecordingChatClient recorder, string error)
    {
        var outcome = new ModelCheckOutcome
        {
            Phrase = phrase,
            Error = error,
            Tools = Called(recorder),
            Responses = [.. recorder.Responses]
        };
        outcome.CountTokens(recorder);

        return outcome;
    }

    /// <summary>Строка отчёта; признаки, которых у фразы нет, — пустые, а не «попал»</summary>
    public ModelCheckPhraseDto ToResult(decimal cost) => new()
    {
        Phrase = Phrase.Phrase,
        ExpectedTool = Phrase.Tool,
        Tools = [.. Tools],
        Answer = Answer.Length <= AnswerMaxLength ? Answer : Answer[..AnswerMaxLength] + "…",
        Error = Error,
        Steps = Steps,
        TokensIn = TokensIn,
        TokensOut = TokensOut,
        Cost = cost,
        ToolHit = ToolHit,
        CleanAnswer = Phrase.Forbidden == null ? null : CleanAnswer,
        RussianAnswer = RussianAnswer
    };

    private static List<string> Called(RecordingChatClient recorder) =>
        [.. recorder.Responses.SelectMany(x => x.Calls).Select(x => x.Name).Distinct()];

    private void CountTokens(RecordingChatClient recorder)
    {
        TokensIn = recorder.TokensIn;
        TokensCached = recorder.TokensCached;
        TokensOut = recorder.TokensOut;
    }
}
