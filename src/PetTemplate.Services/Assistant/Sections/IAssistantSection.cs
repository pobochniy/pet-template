using PetTemplate.Db.Entity;
using PetTemplate.Services.Assistant.Check;

namespace PetTemplate.Services.Assistant.Sections;

/// <summary>
/// Раздел проекта для ассистента — точка расширения целиком (спец. assistant, 7.1):
/// инструменты, операции плана, глаголы трассы, фразы и данные сверки модели одним
/// классом и одной регистрацией — <c>services.AddAssistantSection&lt;T&gt;()</c>.
/// Правка ядра — <c>AssistantTools</c>, <c>PlanExecutor</c>, <c>TraceSummary</c>,
/// <c>ModelCheckRunner</c> — под раздел не нужна.
///
/// Класс раздела — описание без зависимостей: он создаётся при регистрации и живёт
/// синглтоном. Мир сверки модели (спец. admin, 15.3) собирает свой контейнер и зовёт
/// <see cref="Register"/> раздела ещё раз, поэтому всё, что нужно инструментам
/// и операциям раздела (его сервисы), регистрируется здесь же, а не в другом месте
/// </summary>
public interface IAssistantSection
{
    /// <summary>
    /// Инструменты (<c>section.Tools&lt;T&gt;()</c>), операции плана
    /// (<c>section.Operation&lt;T&gt;()</c>) и сервисы, которые им нужны (<c>section.Services</c>)
    /// </summary>
    void Register(AssistantSectionBuilder section);

    /// <summary>
    /// Глаголы «Показать ход мысли» (13.2) по имени инструмента: <c>find_notes</c> →
    /// «искал заметки». Инструмент без глагола показывается своим именем; глаголы ядра
    /// раздел не переписывает
    /// </summary>
    IReadOnlyDictionary<string, string> Verbs => new Dictionary<string, string>();

    /// <summary>
    /// Фразы сверки модели про раздел (спец. admin, 15.3) — идут в набор после фраз ядра
    /// </summary>
    IReadOnlyList<ModelCheckPhrase> Phrases => [];

    /// <summary>
    /// Данные мира сверки под фразы раздела — записи, о которых фразы говорят. Мир живёт
    /// в транзакции, которую откатывают после фразы: в базу ничего не уходит
    /// </summary>
    /// <param name="context">Контекст мира — внутри его транзакции</param>
    /// <param name="workspaceId">Пространство мира</param>
    /// <param name="userId">Автор хода, у него все права</param>
    /// <param name="ct">Остановка воркера</param>
    Task Seed(ApplicationContext context, Guid workspaceId, Guid userId, CancellationToken ct) => Task.CompletedTask;
}
