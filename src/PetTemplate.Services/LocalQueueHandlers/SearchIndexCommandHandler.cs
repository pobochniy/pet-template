using LocalQueue.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Commands;
using PetTemplate.Services.Qdrant;
using PetTemplate.Services.Search;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Services.LocalQueueHandlers;

/// <summary>
/// Индексация сущностей (спец. search, 4): сохранили — команда в очередь,
/// обработчик считает векторы чанков и кладёт точки в Qdrant.
///
/// Обработчик один на все типы — «как индексируется сущность» должно жить в одном месте,
/// иначе правило нарезки разъедется по копиям. Что у сущности за текст и в каком она
/// пространстве, знает её <see cref="ISearchSource"/>: типы приходят регистрацией,
/// а не <c>switch</c> по типу.
///
/// Обработчик идемпотентен по построению: чанки сущности всегда переписываются целиком.
/// Эмбеддер лёг — команда падает и повторяется очередью, как письмо при упавшем SMTP
/// </summary>
public class SearchIndexCommandHandler(
    ApplicationContext context,
    IEnumerable<ISearchSource> sources,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    QdrantService qdrant,
    IOptions<SearchOptions> options) : ICommandHandler<SearchIndexCommand>,
    ICommandHandler<SearchReindexCommand>,
    ICommandHandler<SearchPurgeWorkspaceCommand>
{
    // Два источника на один тип — ошибка регистрации: падаем сразу, а не индексируем
    // каким-то одним из двух
    private readonly Dictionary<SearchEntityType, ISearchSource> _sources = sources.ToDictionary(x => x.Type);

    public async Task Handle(SearchIndexCommand command, CancellationToken ct) =>
        await Index(command.EntityType, command.EntityId, ct);

    /// <summary>
    /// Полный реиндекс (спец. search, 5) — не проход по всей базе одним вызовом, а команда
    /// на каждую сущность. Так обрыв стоит одной сущности, а не всего прохода, эмбеддер
    /// разбирает их с той же скоростью, что и обычную индексацию, а «как индексируется
    /// сущность» живёт в одном месте — в <see cref="Index"/>.
    ///
    /// Сначала — сироты, которых пообъектная индексация не найдёт: точки типов без источника
    /// (тип переименовали или убрали) и пространств, которых нет в базе (команда сноса
    /// исчерпала попытки). Чистка идемпотентна: упадёт — повтор команды пройдёт её заново,
    /// не поставив команд сущностям дважды
    /// </summary>
    public async Task Handle(SearchReindexCommand command, CancellationToken ct)
    {
        await RemoveOrphans(ct);

        foreach (var source in _sources.Values)
        {
            foreach (var id in await source.AllIds(ct))
            {
                context.Enqueue(SearchIndex.Of(source.Type, id));
            }
        }

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Снос пространства (спец. search, 4): его точки уходят по фильтру <c>workspaceId</c>,
    /// какие бы сущности их ни положили. Эмбеддер для этого не нужен — чистится и индекс,
    /// оставшийся от времён, когда он был настроен
    /// </summary>
    public async Task Handle(SearchPurgeWorkspaceCommand command, CancellationToken ct) =>
        await qdrant.DeleteByWorkspace(options.Value.EmbeddingsCollection, command.WorkspaceId, ct);

    /// <summary>
    /// Эмбеддер не настроен — индекс не ведётся, и чистить его не нужно: так же молчит
    /// <see cref="Index"/>
    /// </summary>
    private async Task RemoveOrphans(CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.EmbeddingsEndpoint)) return;

        await qdrant.DeleteUnknownTypes(settings.EmbeddingsCollection, _sources.Keys, ct);

        var indexed = await qdrant.IndexedWorkspaces(settings.EmbeddingsCollection, ct);
        var alive = await context.Workspaces
            .Where(x => indexed.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct);
        foreach (var gone in indexed.Except(alive))
        {
            await qdrant.DeleteByWorkspace(settings.EmbeddingsCollection, gone, ct);
        }
    }

    private async Task Index(SearchEntityType type, string entityId, CancellationToken ct)
    {
        var settings = options.Value;

        // Эмбеддер не настроен — индекса просто нет, и это не ошибка: поиск
        // тогда находит только по словам, а остальное работает
        if (string.IsNullOrWhiteSpace(settings.EmbeddingsEndpoint)) return;

        // Коллекцию здесь не заводим: её создаёт миграция на старте приложения
        // (QdrantInitializer), и «как устроена коллекция» живёт в одном месте.
        // Источника у типа нет (раздел убрали) — как и сущности нет: точки уносим
        var found = _sources.TryGetValue(type, out var source) ? await source.Load(entityId, ct) : null;

        // Сущности больше нет либо текста в ней не осталось — уносим её точки:
        // удаление приходит той же командой
        var chunks = found == null ? [] : SearchChunker.Split(found.Text, settings.ChunkTokens);
        if (found == null || chunks.Count == 0)
        {
            await qdrant.DeleteByEntity(settings.EmbeddingsCollection, type, entityId, ct);

            return;
        }

        var vectors = await embedder.GenerateAsync(chunks.Select(x => x.Text), EmbeddingPurpose.ForPassage(), ct);

        // Эмбеддер ответил не на всё — команда падает и повторяется: записать часть
        // чанков значило бы молча потерять хвост текста
        if (vectors.Count != chunks.Count)
            throw new InvalidOperationException(
                $"Embedder returned {vectors.Count} vectors for {chunks.Count} chunks of {type} {entityId}");

        await qdrant.UpsertChunks(
            settings.EmbeddingsCollection,
            type,
            entityId,
            found.WorkspaceId,
            [.. chunks.Select((chunk, i) => (chunk.Index, vectors[i].Vector.ToArray(), chunk.Text))],
            ct);
    }
}
