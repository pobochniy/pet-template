using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto.Admin;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;
using PetTemplate.Tests.EntityBuilders;

namespace PetTemplate.Tests.Features.Admin;

/// <summary>
/// Порядок моделей (спец. admin, 15.2) и гонки правки справочника: номера уникальны,
/// и обмен двух соседей упирается в индекс; две правки одной строки разводит <c>xmin</c>
/// </summary>
public class AdminModelsDbTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly PetTemplate.Db.Entity.Profile _oleg = new UserBuilder("Oleg").Please().profile;

    private readonly AiModel _seed = AssistantWorld.AiModel("Основная", 1, Now);

    private readonly AiModel _local = AssistantWorld.AiModel("Локальная", 2, Now);

    /// <summary>
    /// Запасная становится основной: номера меняются местами одной транзакцией.
    /// Что ход после этого идёт первой по порядку — <c>ModelBreakerTests</c>
    /// </summary>
    [Fact]
    public async Task ReorderPutsBackupFirst()
    {
        // Arrange
        var (client, db) = await Client();

        // Act
        var response = await client.PutAsJsonAsync("/api/Admin/Models/Order", new[] { _local.Id, _seed.Id });

        // Assert
        await response.ShouldBeSuccessful();
        var order = await db.AiModels.AsNoTracking().OrderBy(x => x.Priority).Select(x => new { x.Id, x.Priority }).ToListAsync();
        Assert.Equal([new { _local.Id, Priority = 1 }, new { _seed.Id, Priority = 2 }], order);
    }

    /// <summary>Кто переставлял — пишется тем строкам, чей номер и правда сменился</summary>
    [Fact]
    public async Task ReorderRecordsWhoMovedModels()
    {
        // Arrange
        var (client, db) = await Client();

        // Act
        var response = await client.PutAsJsonAsync("/api/Admin/Models/Order", new[] { _local.Id, _seed.Id });

        // Assert
        await response.ShouldBeSuccessful();
        var seed = await db.AiModels.AsNoTracking().SingleAsync(x => x.Id == _seed.Id);
        Assert.Equal(_oleg.Id, seed.UpdatedByUserId);
    }

    /// <summary>
    /// Список без одной из моделей — это экран, открытый до чужой правки: порядок
    /// не трогается вовсе
    /// </summary>
    [Fact]
    public async Task ReorderRefusesIncompleteList()
    {
        // Arrange
        var (client, db) = await Client();

        // Act
        var response = await client.PutAsJsonAsync("/api/Admin/Models/Order", new[] { _local.Id });

        // Assert
        await response.ShouldBeBadRequest(
            "Порядок должен перечислять все модели справочника ровно по разу — обновите экран: справочник уже поменялся");
        var seed = await db.AiModels.AsNoTracking().SingleAsync(x => x.Id == _seed.Id);
        Assert.Equal(1, seed.Priority);
    }

    /// <summary>
    /// Две модели, заведённые одновременно, обе встают в конец со своими номерами:
    /// проигравший гонку за «последний + 1» пересчитывает номер, а не отвечает 500
    /// </summary>
    [Fact]
    public async Task ConcurrentCreatesGetDistinctPriorities()
    {
        // Arrange
        var (client, db) = await Client();
        var models = new[] { "Локальная 3090", "Локальная 4090" }.Select(name => new AdminModelEditDto
        {
            Provider = "local",
            Key = "qwen3-30b",
            DisplayName = name,
            CostIn = 0.05m,
            CostCached = 0.05m,
            CostOut = 0.05m,
            IsEnabled = true
        });

        // Act
        var responses = await Task.WhenAll(models.Select(x => client.PostAsJsonAsync("/api/Admin/Models", x)));

        // Assert
        foreach (var response in responses) await response.ShouldBeSuccessful();
        var priorities = await db.AiModels.AsNoTracking().OrderBy(x => x.Priority).Select(x => x.Priority).ToListAsync();
        Assert.Equal([1, 2, 3, 4], priorities);
    }

    /// <summary>
    /// Два админа правят одну строку с одного и того же экрана одновременно: проходит
    /// одна правка, вторая получает «откройте заново», а не перезаписывает первую
    /// </summary>
    [Fact]
    public async Task ConcurrentEditsOneWins()
    {
        // Arrange
        var (client, db) = await Client();
        var version = await db.AiModels.AsNoTracking().Where(x => x.Id == _seed.Id).Select(x => x.Version).SingleAsync();
        var edits = new[] { 0.4m, 0.7m }.Select(cost => new AdminModelEditDto
        {
            Provider = _seed.Provider,
            Key = _seed.Key,
            DisplayName = _seed.DisplayName,
            CostIn = cost,
            IsEnabled = true,
            Version = version
        });

        // Act
        var responses = await Task.WhenAll(edits.Select(x => client.PutAsJsonAsync($"/api/Admin/Models/{_seed.Id}", x)));

        // Assert
        Assert.Single(responses, x => x.IsSuccessStatusCode);
        Assert.Single(responses, x => x.StatusCode == System.Net.HttpStatusCode.BadRequest);
    }

    private async Task<(HttpClient client, ApplicationContext db)> Client() =>
        await Given.AdminClient(_oleg.Id, [AdminRole.Models], db =>
        {
            db.Profiles.Add(_oleg);
            db.AiModels.AddRange(_seed, _local);
            db.UserInAdminRoles.AddRange(Given.AdminRoles(_oleg.Id, AdminRole.Models));
        });
}
