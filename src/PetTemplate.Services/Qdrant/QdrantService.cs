using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace PetTemplate.Services.Qdrant;

/// <summary>
/// Векторный индекс (спец. search, 3 и 6). В нём лежат сущности всех разделов — коллекция
/// одна, а тип сущности у точки в payload.
///
/// <b>Qdrant не знает про пространства:</b> коллекция одна на всех, и вектор чужого
/// пространства лежит рядом с вашим. Забытый фильтр здесь — утечка чужого содержимого,
/// самая дорогая ошибка, которую в этой фиче можно сделать. Поэтому метода поиска без
/// фильтра в этом классе <b>не существует</b>: <c>workspaceId</c> стоит обязательным
/// параметром, а фильтр подставляется внутри и снять его снаружи нечем.
///
/// Синглтон: <c>QdrantClient</c> держит gRPC-канал и рассчитан на общее использование.
///
/// Саму коллекцию и её payload-индексы заводит миграция
/// (<c>EmbeddingsCollectionMigration</c>) на старте приложения — здесь точки только
/// кладутся и читаются.
/// </summary>
public sealed class QdrantService(IOptions<QdrantOptions> options) : IDisposable
{
    /// <summary>
    /// Потолок различных пространств в одной выборке по индексу <c>workspaceId</c> при
    /// реиндексе (спец. search, 5): с запасом на шаблон; дальше — постраничный обход
    /// </summary>
    private const ulong WorkspaceFacetLimit = 100_000;

    private readonly QdrantClient _client = new(host: options.Value.Host, port: options.Value.Port);

    /// <summary>
    /// Переписывает чанки сущности целиком: сущность считаем неделимой единицей
    /// индекса, поэтому старые точки сначала удаляются по фильтру, а потом кладутся
    /// новые. Так правка не оставляет за собой обрывков прежнего текста
    /// </summary>
    public async Task UpsertChunks(
        string collection,
        SearchEntityType type,
        string entityId,
        Guid workspaceId,
        IReadOnlyList<(int Index, float[] Vector, string Text)> chunks,
        CancellationToken ct)
    {
        await DeleteByEntity(collection, type, entityId, ct);
        if (chunks.Count == 0) return;

        var points = chunks.Select(chunk =>
        {
            var point = new PointStruct
            {
                // Идентификатор точки выводится из сущности и номера чанка: повтор
                // команды перезаписывает те же точки, а не плодит новые
                Id = new PointId { Uuid = PointIdOf(type, entityId, chunk.Index).ToString() },
                Vectors = chunk.Vector
            };
            point.Payload.Add(QdrantConst.TypeKey, new Value { StringValue = type.ToString() });
            point.Payload.Add(QdrantConst.EntityKey, new Value { StringValue = entityId });
            point.Payload.Add(QdrantConst.WorkspaceKey, new Value { StringValue = workspaceId.ToString() });
            point.Payload.Add(QdrantConst.ChunkKey, new Value { IntegerValue = chunk.Index });
            point.Payload.Add(QdrantConst.TextKey, new Value { StringValue = chunk.Text });

            return point;
        }).ToList();

        await _client.UpsertAsync(collection, points, cancellationToken: ct);
    }

    public async Task DeleteByEntity(string collection, SearchEntityType type, string entityId, CancellationToken ct)
    {
        var filter = new Filter
        {
            Must =
            {
                Is(QdrantConst.TypeKey, type.ToString()),
                Is(QdrantConst.EntityKey, entityId)
            }
        };

        await _client.DeleteAsync(collection, filter, cancellationToken: ct);
    }

    /// <summary>
    /// Все точки пространства — при его сносе (спец. search, 4): в payload точки лежит текст
    /// чанка, и содержимое снесённого пространства не должно оставаться в индексе.
    /// Коллекции ещё нет — удалять нечего
    /// </summary>
    public async Task DeleteByWorkspace(string collection, Guid workspaceId, CancellationToken ct)
    {
        if (!await _client.CollectionExistsAsync(collection, ct)) return;

        var filter = new Filter { Must = { Is(QdrantConst.WorkspaceKey, workspaceId.ToString()) } };

        await _client.DeleteAsync(collection, filter, cancellationToken: ct);
    }

    /// <summary>
    /// Точки типов, которых нет среди источников (спец. search, 5 и 10): тип переименовали
    /// или убрали, и ни поиск, ни реиндекс их больше не находят. <see cref="SearchEntityType.None"/>
    /// в индекс не попадает и в фильтр входит всегда — так он не бывает пустым, а пустой
    /// фильтр совпадает со всеми точками и без источников
    /// </summary>
    public async Task DeleteUnknownTypes(string collection, IEnumerable<SearchEntityType> known, CancellationToken ct)
    {
        if (!await _client.CollectionExistsAsync(collection, ct)) return;

        var names = new RepeatedStrings();
        names.Strings.AddRange(known.Append(SearchEntityType.None).Distinct().Select(x => x.ToString()));
        var filter = new Filter
        {
            MustNot =
            {
                new Condition
                {
                    Field = new FieldCondition { Key = QdrantConst.TypeKey, Match = new Match { Keywords = names } }
                }
            }
        };

        await _client.DeleteAsync(collection, filter, cancellationToken: ct);
    }

    /// <summary>
    /// Пространства, у которых в коллекции есть точки, — по payload-индексу <c>workspaceId</c>
    /// (спец. search, 5): реиндекс сверяет их с базой и уносит точки снесённых
    /// </summary>
    public async Task<IReadOnlyList<Guid>> IndexedWorkspaces(string collection, CancellationToken ct)
    {
        if (!await _client.CollectionExistsAsync(collection, ct)) return [];

        var facets = await _client.FacetAsync(collection, QdrantConst.WorkspaceKey,
            limit: WorkspaceFacetLimit, exact: true, cancellationToken: ct);

        return
        [
            .. facets.Hits
                .Select(x => Guid.TryParse(x.Value.StringValue, out var id) ? id : Guid.Empty)
                .Where(x => x != Guid.Empty)
        ];
    }

    /// <param name="workspaceId">
    /// Обязательный: фильтр по нему — не опция, а условие существования
    /// этого метода (спец. search, 6)
    /// </param>
    /// <param name="types">
    /// Какие сущности искать. Пусто — все: коллекция одна на модель,
    /// и «найди среди задач» без этого фильтра принесло бы ещё и заметки
    /// </param>
    /// <param name="scoreThreshold">
    /// Нижний порог близости: дальних соседей Qdrant отрежет сам. <c>null</c> — без порога
    /// </param>
    public async Task<List<SearchHit>> Search(
        string collection,
        Guid workspaceId,
        IReadOnlyCollection<SearchEntityType> types,
        float[] vector,
        ulong limit,
        CancellationToken ct,
        float? scoreThreshold = null)
    {
        if (!await _client.CollectionExistsAsync(collection, ct)) return [];

        var filter = new Filter { Must = { Is(QdrantConst.WorkspaceKey, workspaceId.ToString()) } };

        if (types.Count > 0)
        {
            var byType = new Filter();
            foreach (var type in types) byType.Should.Add(Is(QdrantConst.TypeKey, type.ToString()));
            filter.Must.Add(new Condition { Filter = byType });
        }

        var found = await _client.QueryAsync(
            collection,
            vector,
            filter: filter,
            scoreThreshold: scoreThreshold,
            limit: limit,
            payloadSelector: true,
            cancellationToken: ct);

        return
        [
            .. found.Select(x => new SearchHit
            {
                Type = Enum.Parse<SearchEntityType>(x.Payload[QdrantConst.TypeKey].StringValue),
                EntityId = x.Payload[QdrantConst.EntityKey].StringValue,
                Chunk = (int)x.Payload[QdrantConst.ChunkKey].IntegerValue,
                Score = x.Score,
                Text = x.Payload[QdrantConst.TextKey].StringValue
            })
        ];
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Детерминированный идентификатор точки из типа, сущности и номера чанка —
    /// MD5 по их тройке, разложенный в <c>Guid</c>. Криптостойкость здесь ни при чём:
    /// нужна только повторяемость. Тип в ключе не для красоты: у разных разделов
    /// идентификаторы — свои счётчики, и запись 7 одного раздела совпала бы с записью 7 другого
    /// </summary>
    public static Guid PointIdOf(SearchEntityType type, string entityId, int index)
    {
        var source = $"{type}:{entityId}:{index}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(source));

        return new Guid(hash);
    }

    private static Condition Is(string key, string value) =>
        new() { Field = new FieldCondition { Key = key, Match = new Match { Keyword = value } } };
}
