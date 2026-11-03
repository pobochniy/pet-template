using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PetTemplate.Api.Middlewares;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Exceptions;

namespace PetTemplate.Tests.Features.Validation;

/// <summary>
/// Модель ошибок (<see cref="GlobalExceptionHandler"/>): статус дают только свои исключения
/// (<see cref="DomainException"/>) и испорченный HTTP-запрос, всё остальное — 500 с общей фразой
/// и <c>traceId</c>, без текста исключения
/// </summary>
public class ErrorResponseUnitTests
{
    [Fact]
    public async Task BadRequestCarriesCode()
    {
        var (status, body) = await Handle(new BadRequestException(WorkspaceMessages.MemberNotFound));

        Assert.Equal(400, status);
        Assert.Equal(WorkspaceMessages.MemberNotFound.Code, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AdminRejectionCarriesTextWithoutCode()
    {
        var (status, body) = await Handle(new AdminRejectionException("Неизвестная роль"));

        Assert.Equal(400, status);
        Assert.Equal("Неизвестная роль", body.GetProperty("error").GetString());
        Assert.False(body.TryGetProperty("code", out _));
    }

    [Fact]
    public async Task NotFoundIs404()
    {
        var (status, _) = await Handle(new NotFoundException(WorkspaceMessages.MemberNotFound));

        Assert.Equal(404, status);
    }

    /// <summary>
    /// <see cref="ArgumentException"/> и <see cref="InvalidOperationException"/> бросают библиотеки
    /// и <c>SingleAsync</c> — это баг, а не отказ: текст исключения наружу не едет
    /// </summary>
    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(KeyNotFoundException))]
    public async Task ForeignExceptionIsInternalErrorWithoutDetails(Type type)
    {
        var (status, body) = await Handle((Exception)Activator.CreateInstance(type, "Sequence contains no elements")!);

        Assert.Equal(500, status);
        Assert.Equal(GlobalExceptionHandler.InternalErrorText, body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    private static async Task<(int status, JsonElement body)> Handle(Exception exception)
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var handler = new GlobalExceptionHandler(_ => throw exception, NullLogger<GlobalExceptionHandler>.Instance);

        await handler.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        return (context.Response.StatusCode, body);
    }
}
