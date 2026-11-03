using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace PetTemplate.Services.Services;

/// <summary>
/// Транзакция, которая умеет быть вложенной (спец. infrastructure, 3.1): транзакции у контекста
/// ещё нет — своя, обычная; уже есть — точка сохранения внутри неё. Фиксация вложенной
/// отпускает точку, откат и незафиксированный конец — откатывают к ней.
///
/// Нужна там, где код, писанный под свою транзакцию, работает и внутри чужой: ход
/// ассистента в мире сверки модели (спец. admin, 15.3) идёт целиком в транзакции, которую
/// откатывают после фразы, а применение плана и конец хода открывают свои. Postgres
/// вложенных <c>BEGIN</c> не знает, а точки сохранения — знает
/// </summary>
public sealed class NestedTransaction : IAsyncDisposable
{
    private readonly IDbContextTransaction? _own;

    private readonly IDbContextTransaction? _outer;

    private readonly string _savepoint = string.Empty;

    private bool _finished;

    private NestedTransaction(IDbContextTransaction? own, IDbContextTransaction? outer, string savepoint)
    {
        _own = own;
        _outer = outer;
        _savepoint = savepoint;
    }

    public static async Task<NestedTransaction> Begin(DatabaseFacade database, CancellationToken ct)
    {
        var outer = database.CurrentTransaction;
        if (outer == null) return new NestedTransaction(await database.BeginTransactionAsync(ct), null, string.Empty);

        var savepoint = "nested_" + Guid.NewGuid().ToString("N");
        await outer.CreateSavepointAsync(savepoint, ct);

        return new NestedTransaction(null, outer, savepoint);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_own != null) await _own.CommitAsync(ct);
        else await _outer!.ReleaseSavepointAsync(_savepoint, ct);

        _finished = true;
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        if (_own != null) await _own.RollbackAsync(ct);
        else await _outer!.RollbackToSavepointAsync(_savepoint, ct);

        _finished = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_own != null)
        {
            await _own.DisposeAsync();
            return;
        }

        // Не зафиксировали — как у обычной транзакции, сделанное внутри не остаётся
        if (!_finished) await _outer!.RollbackToSavepointAsync(_savepoint, CancellationToken.None);
    }
}
