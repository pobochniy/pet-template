using System.Security.Cryptography;
using System.Text;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;
using Microsoft.Extensions.Options;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Предохранители цикла (спец. assistant, раздел 8). Набор один на все ходы:
/// режимов нет, и таблицы «слева коротко, справа длинно» тоже.
///
/// Состояние живёт ровно один ход — класс scoped, как и всё остальное в ходе.
/// </summary>
public class LoopGuards(IOptions<AssistantOptions> options)
{
    /// <summary>
    /// На третий одинаковый вызов модели подмешивается предупреждение,
    /// на пятый ход обрывается (раздел 8)
    /// </summary>
    private const int RepeatWarning = 3;

    private const int RepeatBreak = 5;

    /// <summary>
    /// За сколько шагов до потолка модель узнаёт, что бюджет кончается. Без этого
    /// она узнаёт о потолке, когда ход уже оборван, и доля обрывов по <c>StepLimit</c>
    /// состоит наполовину из ходов, которым не хватило одного шага на сводку
    /// </summary>
    private const int BudgetWarningAt = 3;

    private readonly AssistantOptions _options = options.Value;
    private readonly Dictionary<string, int> _calls = [];

    /// <summary>Шаг — это один вызов инструмента, им же нумерована трасса (раздел 3)</summary>
    public int Step { get; private set; }

    public long TokensIn { get; private set; }

    public long TokensOut { get; private set; }

    /// <summary>Ход перешагнул малый бюджет и получил большой потолок (раздел 3)</summary>
    public bool Escalated { get; private set; }

    /// <summary>Модель пятый раз подряд зовёт одно и то же — дальше идти незачем</summary>
    public bool Looping { get; private set; }

    public int NextStep() => ++Step;

    public void AddTokens(long? input, long? output)
    {
        TokensIn += input ?? 0;
        TokensOut += output ?? 0;
    }

    /// <summary>
    /// Можно ли делать следующий шаг. Бюджета времени у хода нет ни у какого:
    /// от зацикливания защищают счётчики работы, а не секунды на стене (раздел 8)
    /// </summary>
    public bool CanContinue(out AssistantStopReason? reason)
    {
        reason = null;

        if (Step >= _options.StepsHard || Looping)
        {
            reason = AssistantStopReason.StepLimit;

            return false;
        }

        if (TokensIn + TokensOut >= _options.TokensPerRun)
        {
            reason = AssistantStopReason.TokenLimit;

            return false;
        }

        return true;
    }

    /// <summary>
    /// Эскалация детерминированная: попросить её модель не может, объявить заранее
    /// человек не может. Ход упёрся в малый потолок, а инструменты всё ещё зовутся —
    /// потолок поднимается до <c>StepsHard</c>, и в ленте появляется плашка (раздел 3)
    /// </summary>
    public bool ShouldEscalate()
    {
        if (Escalated || Step < _options.StepsSoft) return false;

        Escalated = true;

        return true;
    }

    /// <summary>
    /// Подмешивает к результату инструмента то, что модель обязана узнать до того,
    /// как ход оборвётся: что она повторяется и что бюджет кончается (раздел 8).
    ///
    /// Именно к результату инструмента, а не отдельным сообщением: это единственный
    /// канал, который модель точно прочитает, и он же не ломает чередование ролей.
    /// </summary>
    public string Decorate(string toolName, string arguments, string result)
    {
        var key = $"{toolName}:{Hash(arguments)}";
        var count = _calls[key] = _calls.GetValueOrDefault(key) + 1;

        var notes = new List<string>();

        if (count >= RepeatBreak)
        {
            Looping = true;
            notes.Add("ты зовёшь одно и то же пятый раз — на этом ход заканчивается, " +
                      "скажи человеку, что успел, и что не вышло");
        }
        else if (count >= RepeatWarning)
        {
            notes.Add("ты повторяешь тот же вызов с теми же аргументами — смени подход или заканчивай");
        }

        var left = _options.StepsHard - Step;
        if (left <= BudgetWarningAt && left > 0)
            notes.Add($"осталось {left} шагов: сводись к плану или ответу");

        return notes.Count == 0 ? result : $"{result}\n\n[{string.Join("; ", notes)}]";
    }

    /// <summary>
    /// Таймаут на отдельный вызов — свой, короче общего: зависший запрос к поиску
    /// не должен съедать весь ход (раздел 8)
    /// </summary>
    public CancellationTokenSource ToolTimeout(CancellationToken ct)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        source.CancelAfter(TimeSpan.FromSeconds(_options.ToolTimeoutSeconds));

        return source;
    }

    /// <summary>
    /// Повтор ловится хешем аргументов: держать в памяти сами аргументы незачем,
    /// а сравнивать строками — дороже и подвержено пробелам
    /// </summary>
    private static string Hash(string arguments) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(arguments)));
}
