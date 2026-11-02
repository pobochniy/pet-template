using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetTemplate.Shared.Dto.Assistant;

/// <summary>
/// План ассистента целиком — содержимое колонки <c>assistant_plans.operations</c>
/// (спец. assistant, 12.2.1) и то, из чего клиент детерминированно рисует блок плана
/// (5.1). Модель блок не пишет: она пишет только прозу вокруг него.
///
/// Пример — заметка в новой книге (операции раздела проекта, 7.1):
///
/// <code>
/// {
///   "operations": [
///     { "index": 1, "kind": "CreateNoteBook", "args": { "title": "Рецепты" } },
///     { "index": 2, "kind": "CreateNotePage", "args": { "title": "Шарлотка", "bookLink": "#1" } }
///   ]
/// }
/// </code>
///
/// Две детали формата, на которые опираются применение и рендер:
/// <list type="bullet">
/// <item><description>
/// <c>index</c> нумеруется <b>с единицы</b> и задаёт порядок применения.
/// </description></item>
/// <item><description>
/// Значение <c>"#n"</c> в поле, чьё имя кончается на <c>Link</c>, — ссылка на результат
/// операции номер <c>n</c>. Ссылаться можно только назад, поэтому циклов не бывает
/// по построению; настоящий идентификатор подставляет <c>PlanExecutor</c> после
/// создания сущности.
/// </description></item>
/// </list>
///
/// Вид операции — строка, а не перечисление: в ядре операций нет, их приносят разделы
/// проекта (<c>IPlanOperationHandler</c>, спец. assistant, 7.1), и перечисление
/// в общем проекте пришлось бы править на каждый раздел.
/// </summary>
public class PlanDocument
{
    public List<PlanOperation> Operations { get; set; } = [];
}

/// <summary>
/// Одна операция плана. Аргументы лежат нетипизированным <see cref="JsonElement"/>:
/// их форма зависит от <see cref="Kind"/>, и раскладывать план по спискам разных
/// типов значит переписывать и запись, и чтение на каждый новый инструмент
/// </summary>
public class PlanOperation
{
    /// <summary>
    /// Номер операции, с единицы. Он же порядок применения и цель ссылок <c>"#n"</c>
    /// </summary>
    public int Index { get; set; }

    /// <summary>Вид операции — <c>IPlanOperationHandler.Kind</c> раздела, который её исполнит</summary>
    public string Kind { get; set; } = string.Empty;

    public JsonElement Args { get; set; }
}

/// <summary>
/// Сериализация плана — одни настройки на запись и на чтение (спец. assistant, 12.2.1).
///
/// Отдельным классом, а не настройками где попало: этот JSON переживает перезапуски
/// и правки кода, лежит в базе и уезжает клиенту, поэтому разойтись на запись и чтение
/// он не имеет права.
/// </summary>
public static class PlanJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Кириллица — как есть, а не \uXXXX: этот JSON читают глазами в разборе
        // полётов и целиком отдают модели в незакрытом плане (12.4), а экранирование
        // раздувает его втрое и не даёт ничего — экранируется он не ради безопасности,
        // а ради ASCII-каналов, которых у нас нет
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>Типизированные аргументы — в тело операции</summary>
    public static JsonElement ToArgs<T>(T args) => JsonSerializer.SerializeToElement(args, Options);

    /// <summary>И обратно, при применении плана</summary>
    public static T? FromArgs<T>(JsonElement args) => args.Deserialize<T>(Options);
}
