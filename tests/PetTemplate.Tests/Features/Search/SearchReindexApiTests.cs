using PetTemplate.Services.Commands;
using PetTemplate.Shared.Enums;
using PetTemplate.Tests.Arranges;
using PetTemplate.Tests.Asserts;

namespace PetTemplate.Tests.Features.Search;

/// <summary>
/// Полный реиндекс из админки (спец. search, 5): ставит одну команду, на сущности её
/// раскладывает обработчик (<see cref="SearchIndexWorker.ReindexEnqueuesEveryEntity"/>).
/// Кому открыт маршрут — <c>AdminRoutesApiTests</c>
/// </summary>
public class SearchReindexApiTests
{
    [Fact]
    public async Task ReindexIsQueued()
    {
        // Arrange
        var oleg = Given.SystemAdmin();
        var (client, db) = await Given.AdminClient(oleg.Id, [AdminRole.AdminManagement], x => x.Profiles.Add(oleg));

        // Act
        var response = await client.PostAsync("/api/Admin/Search/Reindex", null);

        // Assert
        await response.ShouldBeSuccessful();
        Assert.Single(await QueueAssert.Commands<SearchReindexCommand>(db));
    }
}
