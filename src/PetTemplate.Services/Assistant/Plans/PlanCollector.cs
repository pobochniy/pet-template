using System.Text.Json;
using System.Text.RegularExpressions;
using PetTemplate.Shared.Dto.Assistant;

namespace PetTemplate.Services.Assistant.Plans;

/// <summary>
/// Копилка операций хода (спец. assistant, 15.2).
///
/// Цикл вызывает <b>все</b> инструменты, которые позвала модель, включая пишущие, —
/// а записи не должно случиться до согласия (5.1). Поэтому тело пишущего инструмента
/// не трогает базу, а кладёт операцию сюда (<see cref="Propose{TArgs}"/>) и отвечает
/// модели «записал в план, покажешь человеку в конце». После выхода из цикла сервис
/// смотрит копилку: есть операции — сохраняется план, нет — уходит просто текст.
///
/// Класс scoped; про базу, про модель и про то, чем кончился ход, он не знает ничего —
/// только реестр операций, чтобы проверить предохранители раздела 8.
/// </summary>
public class PlanCollector(PlanOperations registry)
{
    /// <summary>
    /// Потолок создаваемых сущностей в плане (раздел 8). Он про длину сводки,
    /// которую человек читает впервые: сводку длиннее он не дочитывает, а «да»,
    /// сказанное непрочитанному, — худший исход всей фичи
    /// </summary>
    public const int MaxCreated = 20;

    /// <summary>
    /// Ссылка на результат более ранней операции: <c>"#1"</c>
    /// </summary>
    private static readonly Regex Reference = new(@"^#(\d+)$", RegexOptions.Compiled);

    private readonly List<PlanOperation> _operations = [];

    public IReadOnlyList<PlanOperation> Operations => _operations;

    public int Count => _operations.Count;

    /// <summary>
    /// Сколько сущностей план создаёт — именно это число упирается в <see cref="MaxCreated"/>.
    /// Правка существующего ничего не создаёт и в счёт не идёт
    /// </summary>
    public int CountCreated => _operations.Count(x => registry.Find(x.Kind)?.IsCreating == true);

    /// <summary>
    /// Кладёт операцию в план и отвечает модели — для инструмента раздела. Предохранители
    /// раздела 8 отвечают строкой, а не исключением: упёршись в потолок, модель должна
    /// объяснить человеку, что дел слишком много, а не уронить ход
    /// </summary>
    public string Propose<TArgs>(string kind, TArgs args)
    {
        try
        {
            var index = Add(kind, PlanJson.ToArgs(args));

            return $"записал в план под номером {index}, человек увидит список в конце";
        }
        catch (PlanRejectedException e)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// Кладёт операцию в план и отдаёт её номер: следующая операция может сослаться
    /// на результат этой (<c>"#n"</c>)
    /// </summary>
    /// <exception cref="PlanRejectedException">
    /// Упёрлись в предохранитель раздела 8 или ссылка ведёт не туда. Текст исключения
    /// уедет модели результатом инструмента: она пересоберёт план или объяснит человеку
    /// </exception>
    public int Add(string kind, JsonElement args)
    {
        var handler = registry.Find(kind)
                      ?? throw new PlanRejectedException($"операции «{kind}» нет — собери план из тех, что есть");

        if (handler.IsCreating && CountCreated >= MaxCreated)
            throw new PlanRejectedException(
                $"в плане уже {MaxCreated} дел — больше в одну сводку не поместится; " +
                "предложи человеку разбить это на два разговора");

        if (handler.MaxPerPlan is { } max && _operations.Count(x => x.Kind == kind) >= max)
            throw new PlanRejectedException(
                $"в плане уже {max} операций «{kind}» — больше за раз нельзя; " +
                "предложи человеку разбить это на части или сослаться на уже собранную операцию");

        var index = _operations.Count + 1;
        EnsureReferences(handler, args, index);

        _operations.Add(new PlanOperation { Index = index, Kind = kind, Args = args });

        return index;
    }

    /// <summary>
    /// Ссылка <c>"#n"</c> может указывать только назад и только на операцию-рамку
    /// своего рода (<see cref="IPlanOperationHandler.Frames"/>).
    ///
    /// «Только назад» — это не ограничение, а то, из-за чего циклов не бывает
    /// по построению: план применяется по порядку, и к моменту подстановки результат
    /// более ранней операции уже есть. «Только на свою рамку» — потому что других мест,
    /// где одна операция плана зависит от другой, не бывает: запись знает свою рамку,
    /// и на этом всё.
    ///
    /// Ссылкой считается значение поля, чьё имя кончается на <c>Link</c>: в остальных
    /// полях живёт текст человека, и запись по имени «#2» не должна читаться ссылкой
    /// </summary>
    private void EnsureReferences(IPlanOperationHandler handler, JsonElement args, int index)
    {
        foreach (var value in Links(args))
        {
            var target = TargetOf(value);
            if (target == null) continue;

            if (target <= 0 || target >= index)
                throw new PlanRejectedException(
                    $"ссылка \"{value}\" ведёт вперёд или в пустоту: сослаться можно " +
                    "только на операцию, которая в плане уже есть");

            if (!handler.Frames.Contains(_operations[target.Value - 1].Kind))
                throw new PlanRejectedException(
                    $"ссылка \"{value}\" ведёт не на свою рамку: ссылками связывается только " +
                    "запись со своей рамкой из этого же плана");
        }
    }

    /// <summary>Номер операции, на которую ссылается значение; не ссылка — <c>null</c></summary>
    public static int? TargetOf(string value)
    {
        var match = Reference.Match(value);

        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    /// <summary>
    /// Значения полей-ссылок на любой глубине: проверка не обязана знать имена полей,
    /// только то, что ссылка живёт в поле <c>…Link</c>
    /// </summary>
    public static IEnumerable<string> Links(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        if (IsLink(property.Name)) yield return property.Value.GetString()!;
                        continue;
                    }

                    foreach (var value in Links(property.Value))
                        yield return value;
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var value in Links(item))
                        yield return value;
                }

                break;
        }
    }

    public static bool IsLink(string propertyName) =>
        propertyName.EndsWith("Link", StringComparison.OrdinalIgnoreCase);
}
