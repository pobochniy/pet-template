namespace PetTemplate.Shared.Options;

/// <summary>
/// Эмбеддер и векторный индекс (спец. search, 3). Умолчания стоят инициализаторами,
/// чтобы приложение поднималось и с пустой секцией: без адреса эмбеддера индекса просто
/// нет, поиск тогда только по словам, а остальное работает
/// </summary>
public class SearchOptions
{
    /// <summary>
    /// Адрес эмбеддера с OpenAI-совместимым <c>POST /embeddings</c> — контейнер
    /// <c>embeddingscontainer</c> рядом с API. Пусто — эмбеддер не настроен
    /// </summary>
    public string EmbeddingsEndpoint { get; set; } = string.Empty;

    /// <summary>Модель эмбеддингов: её имя уходит эмбеддеру в каждом запросе</summary>
    public string EmbeddingsModel { get; set; } = string.Empty;

    /// <summary>
    /// Размерность вектора. Не константа: она у каждой модели своя и живёт здесь,
    /// рядом с именем модели
    /// </summary>
    public int EmbeddingsDimension { get; set; } = 384;

    /// <summary>
    /// Имя коллекции в Qdrant. Привязано к <b>модели</b>, а не к сущности
    /// (<c>pettemplate_e5_small_384</c>): в одной коллекции лежат все типы сущностей, они
    /// различаются типом в payload, — а вот несравнимые векторы разных эмбеддеров в одной
    /// коллекции заставили бы поиск тихо врать, что хуже, чем падать
    /// </summary>
    public string EmbeddingsCollection { get; set; } = "pettemplate_e5_small_384";

    /// <summary>
    /// Префикс текста запроса. Семейство e5 обучено с парой <c>query: </c> / <c>passage: </c>,
    /// и карточка модели велит ставить её всегда; в Lad и ASU префиксов не было. Замер
    /// 2026-11-01 разницы в порядке выдачи не показал, но шкала сдвигается — порог
    /// <see cref="VectorMinScore"/> замерен уже с ними (спец. search, 3;
    /// <c>tech-debt/embeddings-model.md</c>). Модель без префиксов (bge-m3) — пустые строки,
    /// новый порог и полный реиндекс: векторы документов считались с префиксом
    /// </summary>
    public string EmbeddingsQueryPrefix { get; set; } = "query: ";

    /// <summary>Префикс индексируемого текста — пара к <see cref="EmbeddingsQueryPrefix"/></summary>
    public string EmbeddingsPassagePrefix { get; set; } = "passage: ";

    /// <summary>
    /// Размер чанка в токенах при индексации — одинаковый у всех сущностей. У
    /// <c>multilingual-e5-small</c> окно 512 токенов, и запас под плотный текст нужен
    /// (<c>tech-debt/embeddings-model.md</c>)
    /// </summary>
    public int ChunkTokens { get; set; } = 300;

    /// <summary>
    /// Нижний порог косинусной близости для векторной половины поиска. Без него ближайший
    /// сосед находится всегда, и на «рецепт пирога» поиск отдаёт запись про забор.
    /// Шкала своя у каждой модели и у каждого способа звать её, поэтому порог живёт рядом
    /// с именем модели и префиксами: у <c>multilingual-e5-small</c> всё сжато в 0.74–0.89,
    /// префиксы сдвигают шкалу вниз на 0.01–0.02. Замер 2026-11-01 с префиксами: подходящие
    /// по смыслу — от 0.815, посторонние — до 0.83 (в ASU без префиксов было 0.84;
    /// <c>tech-debt/embeddings-model.md</c>). Подходящее ниже порога добирает полнотекстовая
    /// половина. Сменили модель или префиксы — замерить заново (спец. search, 7). Ноль — без порога
    /// </summary>
    public float VectorMinScore { get; set; } = 0.83f;
}
