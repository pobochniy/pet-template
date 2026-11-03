namespace PetTemplate.Shared.Options;

/// <summary>
/// Справка (спец. help, 6). Своя секция, а не ключи в <see cref="SearchOptions"/>:
/// к адресу и интервалу опроса добавился порог векторной половины. Эмбеддер — тот же
/// <see cref="SearchOptions.EmbeddingsEndpoint"/>, своей модели справка не заводит
/// </summary>
public class HelpOptions
{
    /// <summary>
    /// Где внутри контейнера лежит опубликованная справка — ссылка <c>current</c>
    /// на выпуск с <c>manifest.json</c> (6.4). Хост монтирует <c>~/pettemplate-help</c>
    /// только на чтение
    /// </summary>
    public string Path { get; set; } = "/app/help/current";

    /// <summary>
    /// Как часто перечитывать манифест. Опрос, а не слежение за файлами: события через
    /// том докера и замену символической ссылки приходят ненадёжно, а минута задержки
    /// здесь ничего не стоит (6.4)
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Первая пауза перед пересборкой того же выпуска, если разделы встали без векторов
    /// (эмбеддер лежал, 6.4). Каждая неудачная попытка удваивает паузу до
    /// <see cref="VectorRetryMaxDelay"/>: долго лежащий эмбеддер не держит опрос на таймаутах
    /// </summary>
    public TimeSpan VectorRetryDelay { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Потолок паузы между пересборками ради недостающих векторов</summary>
    public TimeSpan VectorRetryMaxDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Нижний порог косинусной близости векторной половины. Без порога ближайшие
    /// разделы находятся на любой вопрос, и «в справке этого нет» не прозвучало бы
    /// никогда — ассистент получал бы три случайных раздела и додумывал по ним.
    ///
    /// Число — стартовое, то же, что <see cref="SearchOptions.VectorMinScore"/> под
    /// <c>multilingual-e5-small</c> с префиксами: раздел справки длиннее записи, и близость
    /// у него может быть ниже. Подбирается по доле пустых поисков и заново — при смене
    /// модели или префиксов (<c>tech-debt/embeddings-model.md</c>). Ноль выключает порог
    /// </summary>
    public float VectorThreshold { get; set; } = 0.83f;
}
