namespace PetTemplate.Services.Commands;

/// <summary>
/// Полный реиндекс (спец. search, 5): сменилась модель эмбеддингов или правило нарезки,
/// либо индекс строится впервые поверх накопленных данных. Обработчик не индексирует
/// сам, а ставит <see cref="SearchIndexCommand"/> на каждую сущность
/// </summary>
public class SearchReindexCommand : ICommand
{
    public static string QueueName => "search-reindex";
}
