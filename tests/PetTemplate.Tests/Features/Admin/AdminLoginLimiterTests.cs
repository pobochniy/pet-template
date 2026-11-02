using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Shared.Options;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Лимит входа в админку (спец. admin, 3.3) без HTTP: параллельные попытки не
/// проскакивают лимит, пока проверяется пароль, а удачная место в счёте возвращает
/// </summary>
public sealed class AdminLoginLimiterTests : IDisposable
{
    private const int Limit = 5;

    private readonly Guid _oleg = Guid.CreateVersion7();

    private readonly AdminLoginLimiter _limiter = new(
        Microsoft.Extensions.Options.Options.Create(new AdminOptions
        {
            AdminLoginAttempts = Limit,
            AdminLoginWindow = TimeSpan.FromMinutes(15)
        }));

    public void Dispose() => _limiter.Dispose();

    /// <summary>Шестая неудача за окно не допускается до проверки пароля</summary>
    [Fact]
    public void LocksUserAfterLimit()
    {
        // Arrange
        for (var i = 0; i < Limit; i++) _limiter.TryBegin(_oleg, "10.0.0.1");

        // Act
        var attempt = _limiter.TryBegin(_oleg, "10.0.0.1");

        // Assert
        Assert.Null(attempt);
    }

    [Fact]
    public async Task ParallelAttemptsDoNotPassLimit()
    {
        // Act
        var attempts = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => _limiter.TryBegin(_oleg, "10.0.0.1"))));

        // Assert
        Assert.Equal(Limit, attempts.Count(x => x != null));
    }

    [Fact]
    public void SucceededAttemptIsNotCounted()
    {
        // Arrange
        for (var i = 0; i < Limit + 1; i++) _limiter.TryBegin(_oleg, "10.0.0.1")!.Succeeded();

        // Act
        var attempt = _limiter.TryBegin(_oleg, "10.0.0.1");

        // Assert
        Assert.NotNull(attempt);
    }

    /// <summary>
    /// Перебор логинов с одного адреса запирает и настоящего админа с этого адреса,
    /// но не с другого
    /// </summary>
    [Fact]
    public void AddressAttemptsLockLoginFromSameAddress()
    {
        // Arrange
        for (var i = 0; i < Limit; i++) _limiter.TryBegin(null, "10.0.0.1");

        // Act
        var attempt = _limiter.TryBegin(_oleg, "10.0.0.1");

        // Assert
        Assert.Null(attempt);
        Assert.NotNull(_limiter.TryBegin(_oleg, "10.0.0.2"));
    }

    /// <summary>
    /// Счёт по учётке, а не по строке логина: ник, почта и телефон одного человека
    /// приходят одним идентификатором, и подбор с разных адресов упирается в один лимит
    /// </summary>
    [Fact]
    public void UserAttemptsLockFromAnyAddress()
    {
        // Arrange
        for (var i = 0; i < Limit; i++) _limiter.TryBegin(_oleg, $"10.0.0.{i}");

        // Act
        var attempt = _limiter.TryBegin(_oleg, "10.0.0.200");

        // Assert
        Assert.Null(attempt);
    }

    /// <summary>
    /// Логин без учётки считается только по адресу: своей записи у него нет, и учётку
    /// с другого адреса его неудачи не запирают
    /// </summary>
    [Fact]
    public void UnknownLoginCountsOnlyByAddress()
    {
        // Arrange
        for (var i = 0; i < Limit; i++) _limiter.TryBegin(null, "10.0.0.1");

        // Act
        var attempt = _limiter.TryBegin(_oleg, "10.0.0.2");

        // Assert
        Assert.NotNull(attempt);
    }

    /// <summary>
    /// Продукт — со своими, мягкими порогами (спец. accounts, 8.1), но в тот же счёт:
    /// неудачи в продукте сверх порога админки запирают вход этой учётки в админку,
    /// а сам продукт пускает до своего
    /// </summary>
    [Fact]
    public void ProductFailuresCountForAdmin()
    {
        // Arrange
        for (var i = 0; i < Limit; i++) _limiter.TryBeginProduct(_oleg, $"10.0.0.{i}");

        // Act
        var admin = _limiter.TryBegin(_oleg, "10.0.0.200");
        var product = _limiter.TryBeginProduct(_oleg, "10.0.0.200");

        // Assert
        Assert.Null(admin);
        Assert.NotNull(product);
    }

    /// <summary>Порог продукта на учётку — <see cref="AdminLoginLimiter.ProductAttemptsPerUser"/></summary>
    [Fact]
    public void LocksProductAfterItsLimit()
    {
        // Arrange
        for (var i = 0; i < AdminLoginLimiter.ProductAttemptsPerUser; i++) _limiter.TryBeginProduct(_oleg, $"10.0.0.{i}");

        // Act
        var attempt = _limiter.TryBeginProduct(_oleg, "10.0.0.200");

        // Assert
        Assert.Null(attempt);
    }

    /// <summary>
    /// Перебор с ротацией адресов не растит память процесса: сверх потолка новые записи
    /// не заводятся
    /// </summary>
    [Fact]
    public void KeepsEntriesUnderCeiling()
    {
        // Act
        for (var i = 0; i < AdminLoginLimiter.MaxEntries + 1000; i++) _limiter.TryBegin(null, $"address-{i}");

        // Assert
        Assert.InRange(_limiter.EntryCount, 1, AdminLoginLimiter.MaxEntries);
    }

    /// <summary>
    /// Кэш забит перебором адресов — неудачи по учётке всё равно считаются: место под её
    /// запись освобождается сразу, а не фоновым сжатием
    /// </summary>
    [Fact]
    public void CountsUserWhenFull()
    {
        // Arrange
        for (var i = 0; i < AdminLoginLimiter.MaxEntries; i++) _limiter.TryBegin(null, $"address-{i}");
        for (var i = 0; i < Limit; i++) _limiter.TryBegin(_oleg, $"rotating-{i}");

        // Act
        var attempt = _limiter.TryBegin(_oleg, "rotating-last");

        // Assert
        Assert.Null(attempt);
    }
}
