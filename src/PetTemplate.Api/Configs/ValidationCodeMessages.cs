using System.Collections;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using PetTemplate.Shared.Errors;

namespace PetTemplate.Api.Configs;

/// <summary>
/// Коды вместо готовых английских фраз у встроенных проверок .NET.
///
/// Свои атрибуты (<c>[UserName]</c>, <c>[Password]</c>, <c>[PhoneNumber]</c>) отдают код
/// сами. У встроенных (<c>[Required]</c>, <c>[MaxLength]</c>, <c>[Range]</c>, <c>[Compare]</c>)
/// своего кода нет: их сообщение приходит из ресурсов .NET, и в русском интерфейсе
/// человек видел бы «The Login field is required.».
///
/// Одним местом, а не правкой DTO: часть <c>[Required]</c> в DTO **не написана руками** —
/// при включённом <c>&lt;Nullable&gt;</c> ASP.NET подставляет его каждому не-nullable
/// ссылочному свойству сам, и убрать такой атрибут из DTO нечем.
///
/// <b>Сообщение уезжает удвоенными скобками</b>: <c>ValidationAttribute.FormatErrorMessage</c>
/// прогоняет его через <c>string.Format</c>, а в упакованной строке есть фигурные скобки
/// JSON-подстановок — без удвоения <c>string.Format</c> бросил бы <c>FormatException</c>.
///
/// Атрибут с заданным вручную <c>ErrorMessage</c> не трогаем: это осознанный текст автора —
/// он и уедет текстом
/// </summary>
public sealed class ValidationCodeMessages : IValidationMetadataProvider
{
    public void CreateValidationMetadata(ValidationMetadataProviderContext context)
    {
        foreach (var item in context.ValidationMetadata.ValidatorMetadata)
        {
            if (item is not ValidationAttribute attribute) continue;
            if (attribute.ErrorMessage is not null || attribute.ErrorMessageResourceName is not null) continue;

            var packed = PackedCode(attribute, context.Key.ModelType);
            if (packed is null) continue;

            attribute.ErrorMessage = EscapeBraces(packed);
        }
    }

    /// <summary>
    /// Код с подстановками для встроенного атрибута — или <c>null</c>, если атрибут
    /// не наш: свои отдают код сами, а незнакомому встроенному лучше остаться
    /// со своим текстом, чем получить чужой перевод.
    ///
    /// <c>[MaxLength]</c> и <c>[MinLength]</c> ставятся и на строку, и на набор,
    /// а «максимум 5 символов» про список прав — это уже неправда, поэтому
    /// у них две ветки по типу свойства
    /// </summary>
    private static string? PackedCode(ValidationAttribute attribute, Type modelType) => attribute switch
    {
        RequiredAttribute => ValidationCodes.Required,

        CompareAttribute => ValidationCodes.Mismatch,

        EmailAddressAttribute => ValidationCodes.Email,

        RangeAttribute range =>
            ValidationError.Pack(ValidationCodes.Range, ("min", range.Minimum), ("max", range.Maximum)),

        StringLengthAttribute length => length.MinimumLength > 0
            ? ValidationError.Pack(
                ValidationCodes.StringLength, ("min", length.MinimumLength), ("max", length.MaximumLength))
            : ValidationError.Pack(ValidationCodes.StringLength, ("max", length.MaximumLength)),

        MaxLengthAttribute max => IsCollection(modelType)
            ? ValidationError.Pack(ValidationCodes.MaxItems, ("max", max.Length))
            : ValidationError.Pack(ValidationCodes.StringLength, ("max", max.Length)),

        MinLengthAttribute min => IsCollection(modelType)
            ? ValidationError.Pack(ValidationCodes.MinItems, ("min", min.Length))
            : ValidationError.Pack(ValidationCodes.StringLength, ("min", min.Length)),

        _ => null
    };

    /// <summary>Строка — тоже <see cref="IEnumerable"/>, но длина у неё в символах</summary>
    private static bool IsCollection(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    /// <summary>
    /// <c>{</c> и <c>}</c> удваиваются, чтобы пережить <c>string.Format</c>
    /// внутри <c>FormatErrorMessage</c> и выйти оттуда собой
    /// </summary>
    private static string EscapeBraces(string message) =>
        message.Replace("{", "{{").Replace("}", "}}");
}
