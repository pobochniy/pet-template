using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetTemplate.Shared.Errors;

/// <summary>
/// Одна ошибка поля в ответе 422: <c>{ "userName": [ { "code", "params", "text" } ] }</c>.
/// Едет объектом, а не строкой: <c>min</c> и <c>max</c> приезжают из DTO, зашить
/// их в перевод нельзя, а строка с разделителем — неявный протокол, который
/// разъедется на первом же новом параметре.
///
/// <see cref="Code"/> нет у сообщений, которые кодом не стали: тексты, заданные
/// автором через <c>ErrorMessage</c>, и фразы, которые .NET сформулировал сам мимо
/// <c>ValidationCodeMessages</c>. Они едут в <see cref="Text"/>, и клиент показывает
/// их как есть — под общим кодом с переводом «Неверное значение» человек потерял бы
/// объяснение, что именно не так.
///
/// Код в <c>ModelState</c> кладут и правила модели (<c>IValidatableObject</c> у
/// <c>UserAndPermissionsDto</c>, <c>InvitationCreateDto</c>) — их разбирает тот же
/// <see cref="Parse"/>
/// </summary>
public sealed record ValidationError(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, object>? Params = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Text = null)
{
    /// <summary>
    /// Разделитель кода и подстановок в компактной строке
    /// </summary>
    private const char Separator = '|';

    /// <summary>
    /// Компактная строка для <see cref="System.ComponentModel.DataAnnotations.ValidationResult"/>:
    /// <c>code</c> либо <c>code|{json}</c>. Нести объект <c>ValidationAttribute</c> не умеет —
    /// в результате проверки есть только сообщение, — поэтому код с подстановками
    /// пакуется в строку здесь и распаковывается <b>в одном месте</b>,
    /// при сборке ответа <c>ValidateRequestAttribute</c>. Наружу уезжает уже объект
    /// </summary>
    public string Pack() => Params is null or { Count: 0 }
        ? Code ?? string.Empty
        : $"{Code}{Separator}{JsonSerializer.Serialize(Params)}";

    /// <inheritdoc cref="Pack()"/>
    public static string Pack(string code, params (string Name, object Value)[] parameters) =>
        new ValidationError(code, parameters.ToDictionary(x => x.Name, x => x.Value)).Pack();

    /// <summary>
    /// Обратная операция к <see cref="Pack()"/>. Сообщение не в формате кода — это
    /// готовая фраза: отдаём её текстом без кода
    /// </summary>
    public static ValidationError Parse(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return new ValidationError(ValidationCodes.Invalid);

        var separator = message.IndexOf(Separator);
        var code = separator < 0 ? message : message[..separator];

        if (!LooksLikeCode(code)) return new ValidationError(null, Text: message);

        if (separator < 0) return new ValidationError(code);

        var parameters = JsonSerializer
            .Deserialize<Dictionary<string, JsonElement>>(message[(separator + 1)..])!
            .ToDictionary(x => x.Key, x => (object)x.Value);

        return new ValidationError(code, parameters);
    }

    /// <summary>
    /// Код — это <c>scope.block.element</c>: сегменты из букв и цифр, минимум два,
    /// без пробелов. Фраза («The Login field is required.») под это не подходит
    /// ни пробелами, ни точкой в конце
    /// </summary>
    private static bool LooksLikeCode(string value)
    {
        var segments = value.Split('.');

        return segments.Length >= 2 && Array.TrueForAll(segments, x => x.Length > 0 && x.All(char.IsLetterOrDigit));
    }
}
