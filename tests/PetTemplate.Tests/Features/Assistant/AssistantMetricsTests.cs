using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Help;
using PetTemplate.Services.Search;
using PetTemplate.Shared.Options;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Mocks;

namespace PetTemplate.Tests.Features.Assistant;

/// <summary>
/// Метрики ассистента и справки (спец. assistant, 13.4; help, 10). Каждый тест — со своим
/// именем метра: метр продукта есть у каждого хоста, а тесты идут параллельно
/// </summary>
public sealed class AssistantMetricsTests : IDisposable
{
    private readonly string _meter = "PetTemplate.Assistant.Test." + Guid.NewGuid().ToString("N");

    private readonly string _help = Path.Combine(Path.GetTempPath(), "pettemplate-metrics-help-" + Guid.NewGuid().ToString("N"));

    private readonly ConcurrentBag<(string Name, double Value, Dictionary<string, object?> Tags)> _measured = [];

    private readonly MeterListener _listener = new();

    public AssistantMetricsTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == _meter) listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<int>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Record(i, v, t));
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        if (Directory.Exists(_help)) Directory.Delete(_help, true);
    }

    /// <summary>
    /// Итог хода: токены, причина остановки и длительность инструмента — с меткой
    /// эскалации и именем инструмента
    /// </summary>
    [Fact]
    public async Task RunIsMeasured()
    {
        // Arrange
        var world = await AssistantWorld.Build("Пончики и Плюшки");
        world.Metrics = new AssistantMetrics(_meter);
        var run = world.StartRun();
        await world.Save();
        var chat = new FakeChatClient()
            .Calling("search_help", new { query = "как пригласить" }).Costing(100, 10)
            .Says("Так.").Costing(200, 20);

        // Act
        await world.Runner(chat).Execute(run.Id, null, CancellationToken.None);

        // Assert
        var tokens = Assert.Single(_measured, x => x.Name == "pettemplate.assistant.run.tokens.in");
        Assert.Equal(300, tokens.Value);
        Assert.Equal(false, tokens.Tags["escalated"]);
        var stop = Assert.Single(_measured, x => x.Name == "pettemplate.assistant.run.stop_reason");
        Assert.Equal("Completed", stop.Tags["reason"]);
        var tool = Assert.Single(_measured, x => x.Name == "pettemplate.assistant.tool.duration");
        Assert.Equal("search_help", tool.Tags["tool"]);
    }

    /// <summary>
    /// Поиск по справке с признаком «нашлось» и версия опубликованной справки — коммит
    /// выпуска (спец. help, 10): доля пустых поисков — список дыр в справке
    /// </summary>
    [Fact]
    public async Task HelpSearchIsMeasured()
    {
        // Arrange
        var help = await PublishedHelp(new AssistantMetrics(_meter));

        // Act
        await help.Search("как пригласить участника", CancellationToken.None);
        await help.Search("квантовая хромодинамика", CancellationToken.None);
        _listener.RecordObservableInstruments();

        // Assert
        var searches = _measured.Where(x => x.Name == "pettemplate.assistant.help.search").ToList();
        Assert.Equal([true, false], searches.Select(x => x.Tags["found"]).OrderDescending());
        var version = Assert.Single(_measured, x => x.Name == "pettemplate.assistant.help.index_version");
        Assert.Equal("a1", version.Tags["commit"]);
    }

    private void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        where T : struct =>
        _measured.Add((instrument.Name, Convert.ToDouble(value), tags.ToArray().ToDictionary(x => x.Key, x => x.Value)));

    private async Task<HelpSearch> PublishedHelp(AssistantMetrics metrics)
    {
        Directory.CreateDirectory(_help);
        await File.WriteAllTextAsync(Path.Combine(_help, "workspace.md"), """
            ---
            title: Пространство
            page: /workspace/members
            ---

            ## Как пригласить участника

            На странице участников нажмите «Добавить участника» и отправьте ссылку.
            """);
        await File.WriteAllTextAsync(Path.Combine(_help, HelpManifest.FileName),
            """{"commit":"a1","publishedAt":"2026-11-01T10:00:00Z","articles":[],"hash":"x"}""");

        var help = new HelpSearch(
            new ServiceCollection()
                .AddSingleton<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>,
                    NullEmbedder>()
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new HelpOptions { Path = _help }),
            Options.Create(new SearchOptions()),
            NullLogger<HelpSearch>.Instance,
            metrics)
        {
            CacheDirectory = null
        };
        await help.Refresh(CancellationToken.None);

        return help;
    }
}
