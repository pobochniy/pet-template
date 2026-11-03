namespace PetTemplate.Shared.Errors;

/// <summary>
/// Отказ, который увидит человек: смысловой <see cref="Code"/> для перевода на клиенте
/// и русский <see cref="Text"/>.
///
/// Текст не убираем вместе с переездом на коды: это ответ тому, кто дёргает API
/// напрямую, и страховка на случай отставшего словаря — клиент показывает перевод,
/// если ключ нашёлся, и текст, если нет.
///
/// Первый сегмент кода — имя словаря на клиенте (<c>workspace.error.notAMember</c> →
/// <c>i18n/{lang}/workspace.json</c>); что каждый код есть в словаре, сверяет
/// <c>npm run i18n:codes</c>.
///
/// <see cref="Params"/> — подстановки для формулировки с числом или именем.
/// Подставляет их клиент: собранная на сервере строка была бы уже на русском,
/// и переводить в ней стало бы нечего
/// </summary>
public sealed record ErrorMessage(string Code, string Text, IReadOnlyDictionary<string, object>? Params = null)
{
    /// <summary>
    /// Тот же отказ с подстановками: значения уезжают клиенту отдельным полем,
    /// а в русский текст подставляются тут же — он для тех, кто перевода не ждёт.
    /// Место подстановки в тексте помечено <c>{имя}</c>, как и в словарях клиента
    /// </summary>
    public ErrorMessage With(params (string Name, object Value)[] parameters) =>
        this with
        {
            Text = parameters.Aggregate(Text, (text, p) => text.Replace($"{{{p.Name}}}", p.Value.ToString())),
            Params = parameters.ToDictionary(x => x.Name, x => x.Value)
        };

    /// <summary>
    /// В строковом контексте отказ — это его текст: так он ложится в сообщение
    /// исключения и в логи
    /// </summary>
    public override string ToString() => Text;
}
