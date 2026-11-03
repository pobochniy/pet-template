using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.Assistant;

/// <summary>
/// Память о человеке (спец. assistant, 12.5).
///
/// Отдельный механизм, а не надстройка над компакцией: компакция запускается по длине
/// и по природе теряющая, «Очистить» стирает тред вместе с ней, — а долговечным фактам
/// не место в теряющем артефакте.
///
/// Храним только то, чего нет в таблицах: псевдонимы, регулярные обстоятельства,
/// умолчания. Правило проверки кандидата простое: храним то, что человек согласился бы
/// увидеть написанным про себя.
///
/// Память живёт там же, где тред, — в паре «человек + пространство» (12.1): общие факты
/// пространства видят все его участники, личные — только их владелец, и в другом
/// пространстве того же человека их нет.
/// </summary>
public class MemoryService(ApplicationContext context, IOptions<AssistantOptions> options)
{
    /// <summary>Потолок ключа — как у колонки</summary>
    private const int KeyLength = 64;

    /// <summary>Потолок текста факта — порядка двухсот знаков, как у колонки</summary>
    private const int TextLength = 200;

    /// <summary>
    /// Доля значимых слов общего факта, которые обязаны найтись в реплике человека (12.5):
    /// больше половины — пересказ того, что он сказал, а не чужой текст под его ключом
    /// </summary>
    public const double SharedFactGrounding = 0.6;

    /// <summary>Короче — предлоги и союзы («в», «до», «и»): они не доказывают ничего</summary>
    private const int GroundingWordLength = 3;

    /// <summary>Общая основа двух форм слова — не короче четырёх букв</summary>
    private const int GroundingStemLength = 4;

    /// <summary>
    /// Что едет в промпт: все факты пространства плюс личные автора хода, но не больше
    /// потолка. Тридцать фактов модель ещё различает, триста — уже нет, и «память»
    /// превращается в фон, на котором теряется нужное
    /// </summary>
    public async Task<List<AssistantFact>> ForPrompt(Guid userId, Guid workspaceId, CancellationToken ct) =>
        await Visible(userId, workspaceId)
            .AsNoTracking()
            .OrderByDescending(x => x.UseCount)
            .ThenByDescending(x => x.LastUsedAtUtc)
            .Take(options.Value.MemoryFactsLimit)
            .ToListAsync(ct);

    /// <summary>
    /// Записать факт. Дедупликация по ключу, а не по похожести текста: «Илья — это Илья
    /// Петров» и «Илья Петров — муж сестры» похожи как строки, но ключи у них разные,
    /// и это два разных нужных факта
    /// </summary>
    public async Task<RememberOutcome> Remember(
        Guid workspaceId,
        Guid? userId,
        AssistantFactKind kind,
        string key,
        string text,
        Guid? sourceRunId,
        CancellationToken ct)
    {
        var normalized = Normalize(key);
        if (normalized.Length > KeyLength) normalized = normalized[..KeyLength].TrimEnd();
        if (normalized.Length == 0 || string.IsNullOrWhiteSpace(text)) return new RememberOutcome();

        var existing = await Find(workspaceId, userId, kind, normalized, ct);
        if (existing != null) return await Update(existing, text, sourceRunId, ct);

        var fact = new AssistantFact
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            UserId = userId,
            Kind = kind,
            Key = normalized,
            Text = Trim(text),
            SourceRunId = sourceRunId,
            CreatedAtUtc = DateTime.UtcNow
        };

        var forgotten = await Evict(workspaceId, userId, sourceRunId, ct);

        await context.AssistantFacts.AddAsync(fact, ct);
        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (DbErrors.IsDuplicate(e, DbErrors.AssistantFactKeyIndex))
        {
            // Тот же общий факт в ту же секунду записал ход соседа по пространству.
            // Совпал ключ — значит, это обновление, а не второй факт (12.5); вытеснение
            // отменяется вместе с нашей вставкой
            context.ChangeTracker.Clear();

            var winner = await Find(workspaceId, userId, kind, normalized, ct);
            if (winner == null) throw;

            return await Update(winner, text, sourceRunId, ct);
        }

        return new RememberOutcome { Fact = fact, Forgotten = forgotten };
    }

    /// <summary>
    /// Что факт пригодился — меряется подстрочным поиском по коротким ключам в реплике
    /// человека и ответе ассистента, одной проверкой на факт, без модели и без отдельного
    /// инструмента.
    ///
    /// Мера грубая: факт мог помочь молча, а мог совпасть случайно. Но она различает
    /// «Илью», которого поминают через день, и «отчёт по пятницам»,
    /// о котором не вспомнили ни разу за полгода
    /// </summary>
    public async Task TouchUsed(
        Guid userId,
        Guid workspaceId,
        string? userText,
        string? assistantText,
        CancellationToken ct)
    {
        var haystack = Normalize($"{userText} {assistantText}");
        if (haystack.Length == 0) return;

        var facts = await Visible(userId, workspaceId).ToArrayAsync(ct);
        var used = facts.Where(x => haystack.Contains(x.Key, StringComparison.Ordinal)).ToArray();
        if (used.Length == 0) return;

        var now = DateTime.UtcNow;
        foreach (var fact in used)
        {
            fact.UseCount++;
            fact.LastUsedAtUtc = now;
        }

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Список для экрана фактов: общие для пространства и личные автора — так они
    /// на экране и сгруппированы
    /// </summary>
    public async Task<List<AssistantFact>> List(Guid userId, Guid workspaceId, CancellationToken ct) =>
        await Visible(userId, workspaceId)
            .AsNoTracking()
            .OrderBy(x => x.UserId == null ? 0 : 1)
            .ThenBy(x => x.Key)
            .ToListAsync(ct);

    /// <summary>
    /// Неверный факт удаляют, а не тащат вечно. Чужой личный факт и факт чужого
    /// пространства не удаляются и не отличаются от несуществующего
    /// </summary>
    public async Task<bool> Delete(Guid id, Guid userId, Guid workspaceId, CancellationToken ct) =>
        await Visible(userId, workspaceId).Where(x => x.Id == id).ExecuteDeleteAsync(ct) > 0;

    /// <summary>
    /// Факты по идентификаторам — для строки «Запомнил: …» в ленте
    /// </summary>
    public async Task<List<AssistantFact>> ByIds(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];

        return await context.AssistantFacts.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
    }

    /// <summary>
    /// Текст общего факта взят из реплики человека (12.5, раздел 9): не меньше
    /// <see cref="SharedFactGrounding"/> его значимых слов нашлись в реплике. Слово —
    /// от трёх букв или число; совпадение — до окончания («Насти» и «Настя», «отпуск»
    /// и «отпуске»), а каждое число факта обязано прозвучать целиком. Мера грубая и без модели: пересказ своими
    /// словами она отбивает, зато строка из чужой записи под прозвучавшим ключом
    /// в общую память не уедет
    /// </summary>
    public static bool IsGrounded(string text, string userText)
    {
        var said = Words(userText);
        var words = Words(text).Where(x => x.Length >= GroundingWordLength || x.All(char.IsDigit)).ToArray();
        if (words.Length == 0) return false;

        // Число — дата, сумма, номер — либо сказано, либо выдумано: долей его не добрать
        if (words.Any(word => word.All(char.IsDigit) && !said.Contains(word))) return false;

        var found = words.Count(word => said.Any(x => SameWord(word, x)));

        return found >= words.Length * SharedFactGrounding;
    }

    /// <summary>
    /// Ключ — короткая нормализованная форма того, про что факт: нижний регистр,
    /// без кавычек и знаков препинания. На нём держатся и дедупликация, и счёт
    /// использования, поэтому нормализация одна на обе стороны
    /// </summary>
    public static string Normalize(string key)
    {
        var text = new StringBuilder();
        foreach (var symbol in key.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(symbol) || char.IsWhiteSpace(symbol))
            {
                text.Append(char.IsWhiteSpace(symbol) ? ' ' : symbol);
            }
        }

        return string.Join(' ', text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string[] Words(string text) =>
        Normalize(text).Replace('ё', 'е').Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Одно слово с точностью до окончания: общая основа — всё, кроме двух последних букв</summary>
    private static bool SameWord(string word, string said)
    {
        if (word == said) return true;
        if (word.All(char.IsDigit) || said.All(char.IsDigit)) return false;

        var common = 0;
        while (common < word.Length && common < said.Length && word[common] == said[common]) common++;

        return common >= Math.Max(GroundingStemLength, Math.Min(word.Length, said.Length) - 2);
    }

    private async Task<AssistantFact?> Find(
        Guid workspaceId,
        Guid? userId,
        AssistantFactKind kind,
        string key,
        CancellationToken ct) =>
        await context.AssistantFacts
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId
                                       && x.UserId == userId
                                       && x.Kind == kind
                                       && x.Key == key, ct);

    /// <summary>
    /// Совпал ключ — факт обновляется: «домик — это дача» и «домик — это дача
    /// родителей» несут один ключ, и второй перезаписывает первый
    /// </summary>
    private async Task<RememberOutcome> Update(AssistantFact fact, string text, Guid? sourceRunId, CancellationToken ct)
    {
        fact.Text = Trim(text);
        fact.SourceRunId = sourceRunId;
        await context.SaveChangesAsync(ct);

        return new RememberOutcome { Fact = fact };
    }

    /// <summary>
    /// Упёрлись в потолок — новый факт записывается, а уходит тот, у кого <c>UseCount</c>
    /// меньше всех; при равенстве — тот, чей <c>LastUsedAtUtc</c> старше, а если совпал
    /// и он — самый старый по <c>CreatedAtUtc</c>. Факт, записанный в этом же ходе,
    /// не вытесняется никогда. Потолок — на владельца: общих фактов пространства
    /// и личных у каждого участника — по <c>MemoryFactsLimit</c>
    /// </summary>
    private async Task<AssistantFact?> Evict(
        Guid workspaceId,
        Guid? userId,
        Guid? sourceRunId,
        CancellationToken ct)
    {
        var limit = options.Value.MemoryFactsLimit;

        var owned = await context.AssistantFacts
            .Where(x => x.WorkspaceId == workspaceId && x.UserId == userId)
            .ToArrayAsync(ct);
        if (owned.Length < limit) return null;

        var victim = owned
            .Where(x => sourceRunId == null || x.SourceRunId != sourceRunId)
            .OrderBy(x => x.UseCount)
            .ThenBy(x => x.LastUsedAtUtc ?? DateTime.MinValue)
            .ThenBy(x => x.CreatedAtUtc)
            .FirstOrDefault();
        if (victim == null) return null;

        context.AssistantFacts.Remove(victim);

        return victim;
    }

    /// <summary>
    /// Факты пространства — общие, личные умолчания — при человеке (12.5)
    /// </summary>
    private IQueryable<AssistantFact> Visible(Guid userId, Guid workspaceId) =>
        context.AssistantFacts
            .Where(x => x.WorkspaceId == workspaceId && (x.UserId == null || x.UserId == userId));

    private static string Trim(string text)
    {
        var trimmed = text.Trim();

        return trimmed.Length <= TextLength ? trimmed : trimmed[..TextLength];
    }
}
