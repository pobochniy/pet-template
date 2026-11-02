using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PetTemplate.Shared.Errors;

namespace PetTemplate.Api.Middlewares;

/// <summary>
/// Модель не прошла проверки — <c>422</c> с ошибками по полям:
/// <c>{ "userName": [ { "code", "params", "text" } ] }</c> (<see cref="ValidationError"/>).
/// Клиент переводит код и раскладывает ошибки по полям формы
/// (<c>applyServerValidationErrors</c>); фразу без кода показывает как есть.
///
/// Коды кладут в <c>ModelState</c> и свои атрибуты (<c>[UserName]</c>), и встроенные
/// (<c>ValidationCodeMessages</c>), и правила модели (<c>IValidatableObject</c> у
/// <c>UserAndPermissionsDto</c>) — разбор у всех один, <see cref="ValidationError.Parse"/>
/// </summary>
public class ValidateRequestAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;

        // Тело не разобралось целиком: либо это не JSON, либо значение поля не легло
        // в свой тип (число вне перечисления). Во втором случае имя поля известно
        // и уезжает клиенту, в первом — нет
        var unboundBody = UnboundBodyParameters(context);

        var errors = context.ModelState
            .Where(x => x.Value!.Errors.Count > 0)
            // Жалоба на сам параметр тела наружу не едет: она называет имя аргумента
            // в коде контроллера, а не поле формы
            .Where(x => !unboundBody.Contains(x.Key))
            // Группировка, а не ToDictionary: два ключа ModelState могут дать одно
            // имя поля («$.a.name» и «$.b.name»), и словарь ответил бы на это 500-м
            // вместо 422. Ошибки таких ключей склеиваются в один список
            .GroupBy(x => FieldName(x.Key))
            .ToDictionary(
                x => x.Key,
                // Единственное место, где компактная строка атрибута превращается
                // в объект: дальше по проводу едет уже {code, params} и один разбор
                // на клиенте.
                //
                // У неразобранного тела сообщение писал System.Text.Json — путь,
                // номер байта, ожидаемая глубина. Наружу оно не едет: это утечка
                // деталей парсера ради нулевой аудитории, поле отвечает общим кодом
                x => x.SelectMany(entry => entry.Value!.Errors
                        .Select(e => unboundBody.Count > 0
                            ? BrokenBodyError(entry.Key)
                            : ValidationError.Parse(e.ErrorMessage)))
                    .ToArray()
            );

        // Ни одного поля не назвали — значит тело не JSON вовсе
        if (errors.Count == 0 && unboundBody.Count > 0)
        {
            errors[ValidationFields.Payload] = [new ValidationError(ValidationCodes.MalformedJson)];
        }

        context.Result = new UnprocessableEntityObjectResult(errors);
    }

    /// <summary>
    /// Отказ по полю неразобранного тела: поле названо — значит в нём значение,
    /// которое не легло в свой тип; не названо — сломан весь документ
    /// </summary>
    private static ValidationError BrokenBodyError(string key) =>
        new(CleanPropertyName(key).Length == 0 ? ValidationCodes.MalformedJson : ValidationCodes.Invalid);

    /// <summary>
    /// Имя поля для ответа. Пустое имя бывает у ошибки на весь документ —
    /// её и подписываем «телом запроса».
    ///
    /// Имя приводится к camelCase, потому что приезжает оно двумя путями и в двух
    /// видах: «UserName» — имя свойства DTO, так ключ называет <c>ModelState</c>
    /// после атрибутов; «$.userName» — путь от разбора JSON. По проводу поле
    /// в обоих случаях называется одинаково (camelCase из <c>JsonSerializerDefaults.Web</c>) —
    /// и ключ отказа совпадает с ним, а не с тем, каким путём отказ доехал.
    ///
    /// Сама политика имён до ключей словаря не достаёт (<c>DictionaryKeyPolicy</c>
    /// в веб-дефолтах не задан), поэтому приводим руками
    /// </summary>
    private static string FieldName(string key)
    {
        var name = CleanPropertyName(key);

        return name.Length == 0 ? ValidationFields.Payload : JsonNamingPolicy.CamelCase.ConvertName(name);
    }

    /// <summary>
    /// Параметры из тела запроса, которые связать не удалось. Смотрим не на текст
    /// ошибки, а на факт: аргумента действия нет вовсе
    /// </summary>
    private static HashSet<string> UnboundBodyParameters(ActionExecutingContext context) =>
        context.ActionDescriptor.Parameters
            .Where(x => x.BindingInfo?.BindingSource == BindingSource.Body)
            .Where(x => !context.ActionArguments.ContainsKey(x.Name))
            .Select(x => x.Name)
            .ToHashSet();

    /// <summary>«$.permissions» и «$.dto.permissions» — «permissions»; «UserName» — как есть</summary>
    private static string CleanPropertyName(string key) => key.TrimStart('$', '.').Split('.')[^1];
}
