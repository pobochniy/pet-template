using LocalQueue.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetTemplate.Api.Configs;
using PetTemplate.Db.Entity;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Services.Qdrant;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Containers;
using PetTemplate.Tests.Features.Search.TestNotes;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Search;

/// <summary>
/// Приложение с поиском, подключённым к тестовой сущности (спец. search, 9), — так, как
/// его подключит раздел проекта: свой контекст, <c>AddSearchSource</c>, свой тип.
/// Qdrant — настоящий (<see cref="QdrantServer"/>), коллекция — своя у теста; база — своя
/// у теста, таблица <c>test_notes</c> встаёт в неё рядом со схемой приложения; эмбеддер —
/// <see cref="FakeEmbedder"/>.
///
/// <paramref name="withQueueWorker"/> — разбирать ли очередь: обработчик зовут сами,
/// кроме теста, что команда из очереди и правда доходит до индекса
/// </summary>
public sealed class SearchWorld
{
    private SearchWorld(IServiceProvider services, string collection, FakeEmbedder embedder)
    {
        Services = services;
        Collection = collection;
        Embedder = embedder;
    }

    public IServiceProvider Services { get; }

    public string Collection { get; }

    public FakeEmbedder Embedder { get; }

    public static async Task<SearchWorld> Start(bool withQueueWorker = false, bool withEmbedder = true)
    {
        var collection = await QdrantServer.CreateCollectionAsync(FakeEmbedder.Vector.Length);
        var qdrant = await QdrantServer.OptionsAsync();
        var embedder = new FakeEmbedder();
        var configureAuth = TestSettings.ConfigureAuth();

        var (_, services) = await new ApiApplicationFactory<Program>(withQueueWorker).SetupApplication(
            dbName: Guid.NewGuid().ToString(),
            configureSettings: settings =>
            {
                configureAuth(settings);
                settings["QdrantOptions:Host"] = qdrant.Host;
                settings["QdrantOptions:Port"] = qdrant.Port.ToString();
                // Непустой адрес — признак «эмбеддер настроен»; сам он тут фейковый.
                // Пустой — эмбеддера нет вовсе: поиск только по словам
                settings["SearchOptions:EmbeddingsEndpoint"] = withEmbedder ? "http://embeddings.test/v1" : "";
                settings["SearchOptions:EmbeddingsCollection"] = collection;
                settings["SearchOptions:EmbeddingsDimension"] = FakeEmbedder.Vector.Length.ToString();
                settings["SearchOptions:ChunkTokens"] = "40";
                settings["CommandQueueOptions:PollInterval"] = "00:00:00.100";
            },
            configureServices: services =>
            {
                services.AddDbContext<TestNotesContext>((provider, options) => options
                    .UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("AppConnection"))
                    .UseSnakeCaseNamingConvention());
                services.AddSearchSource<TestNoteSource>();
                services.AddSearchSource<TestDraftSource>();
                services.AddTransient<TestNoteSearch>();
                if (withEmbedder) embedder.Services()(services);
            });

        using (var scope = services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TestNotesContext>().CreateTable();
        }

        return new SearchWorld(services, collection, embedder);
    }

    /// <summary>Заметки — в базу теста одним сохранением</summary>
    public async Task Add(params TestNote[] notes)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestNotesContext>();
        db.Notes.AddRange(notes);
        await db.SaveChangesAsync();
    }

    /// <summary>Правка заметки — новое тело</summary>
    public async Task Rewrite(Guid noteId, string body)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestNotesContext>();
        await db.Notes.Where(x => x.Id == noteId).ExecuteUpdateAsync(x => x.SetProperty(n => n.Body, body));
    }

    public async Task Remove(Guid noteId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestNotesContext>();
        await db.Notes.Where(x => x.Id == noteId).ExecuteDeleteAsync();
    }

    /// <summary>Обработчик очереди — из той же регистрации, что и в бою, своей областью</summary>
    public async Task Handle<TCommand>(TCommand command)
    {
        using var scope = Services.CreateScope();
        var handler = (ICommandHandler<TCommand>)scope.ServiceProvider.GetRequiredService<SearchIndexCommandHandler>();
        await handler.Handle(command, CancellationToken.None);
    }

    /// <summary>Точки пространства в коллекции теста — тем же методом, каким ищет приложение</summary>
    public Task<List<SearchHit>> Points(Guid workspaceId, params SearchEntityType[] types) =>
        Services.GetRequiredService<QdrantService>()
            .Search(Collection, workspaceId, types, FakeEmbedder.Vector, 10, CancellationToken.None);

    public async Task<IReadOnlyList<Guid>> Find(Guid workspaceId, string query)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TestNoteSearch>()
            .Find(workspaceId, query, CancellationToken.None);
    }

    public async Task<IReadOnlyList<Guid>> Words(Guid workspaceId, string query)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TestNoteSearch>()
            .Words(workspaceId, query, CancellationToken.None);
    }

    /// <summary>Контекст приложения — очередь команд</summary>
    public ApplicationContext Db() => Services.GetRequiredService<ApplicationContext>();

    public static TestNote Note(Guid workspaceId, string title, string? body) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkspaceId = workspaceId,
        Title = title,
        Body = body
    };
}
