using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PetTemplate.Shared.Errors;

namespace PetTemplate.Tests.Asserts;

public static class HttpAssert
{
    public static async Task ShouldBeSuccessful(this HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        var errorMsg = await response.Content.ReadAsStringAsync();
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                errorMsg = "Unauthorized";
                break;
            case HttpStatusCode.Forbidden:
                errorMsg = "Forbidden";
                break;
            case HttpStatusCode.UnprocessableEntity:
                errorMsg = "Validation " + errorMsg;
                break;
        }

        if (string.IsNullOrWhiteSpace(errorMsg)) errorMsg = response.ReasonPhrase;

        Assert.True(response.IsSuccessStatusCode, "(http) " + errorMsg);
    }

    /// <summary>
    /// 422 с ошибками по каждому из полей. Имя поля сравнивается без учёта регистра:
    /// сервер отдаёт его в camelCase, как оно едет в JSON (<c>ValidateRequestAttribute</c>),
    /// а тесты называют свойство DTO
    /// </summary>
    public static async Task ShouldBeValidation(this HttpResponseMessage response, IEnumerable<string> requredFields)
    {
        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, "Validation status code");
        var result = await response.Content.ReadFromJsonAsync<Dictionary<string, ValidationError[]>>();
        var fields = new HashSet<string>(result!.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var field in requredFields)
        {
            Assert.True(fields.Contains(field), $"validation [{field}] field");
        }
    }

    /// <summary>
    /// Отказ <c>400</c> с кодом из <paramref name="expected"/> — тот, что бросил сервис
    /// (<c>BadRequestException</c>). Код, а не текст: текст — для человека и может меняться
    /// </summary>
    public static async Task ShouldBeBadRequestWith(this HttpResponseMessage response, ErrorMessage expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"Expected 400, got {(int)response.StatusCode}: {body}");

        var error = JsonSerializer.Deserialize<ErrorBody>(body, JsonSerializerOptions.Web);
        Assert.Equal(expected.Code, error?.Code);
    }

    /// <summary>
    /// Отказ <c>400</c> с текстом <paramref name="expected"/> — так отказывает админка: кодов
    /// она не заводит, текст и есть ответ (спец. admin, 3.5)
    /// </summary>
    public static async Task ShouldBeBadRequest(this HttpResponseMessage response, string expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"Expected 400, got {(int)response.StatusCode}: {body}");

        var error = JsonSerializer.Deserialize<ErrorBody>(body, JsonSerializerOptions.Web);
        Assert.Equal(expected, error?.Error);
    }

    /// <summary>
    /// Отказ <c>422</c> у поля <paramref name="field"/> с кодом <paramref name="code"/>
    /// (<see cref="ValidationError"/>). Ответ — <c>{поле: [{code, params, text}]}</c>, имя поля
    /// в camelCase (<c>ValidateRequestAttribute</c>); сравнивается без учёта регистра —
    /// тесты называют и свойство DTO
    /// </summary>
    public static async Task ShouldBeValidationWith(this HttpResponseMessage response, string field, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, $"Expected 422, got {(int)response.StatusCode}: {body}");

        var errors = JsonSerializer.Deserialize<Dictionary<string, ValidationError[]>>(body, JsonSerializerOptions.Web);
        Assert.NotNull(errors);
        var entry = errors.FirstOrDefault(x => string.Equals(x.Key, field, StringComparison.OrdinalIgnoreCase));
        Assert.True(entry.Value != null, $"validation [{field}] field: {body}");
        Assert.Contains(entry.Value, x => x.Code == code);
    }

    /// <summary>404 с кодом отказа <paramref name="expected"/> — «такого нет», а не пустой 404 маршрута</summary>
    public static async Task ShouldBeNotFoundWith(this HttpResponseMessage response, ErrorMessage expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"Expected 404, got {(int)response.StatusCode}: {body}");

        var error = JsonSerializer.Deserialize<ErrorBody>(body, JsonSerializerOptions.Web);
        Assert.Equal(expected.Code, error?.Code);
    }

    private sealed record ErrorBody(string? Error, int StatusCode, string? Code);
}
