namespace PetTemplate.Help;

/// <summary>
/// Статья справки (спец. help, 3): одна тема, которую человек узнаёт, и её
/// разделы «один вопрос — один раздел». Разбор общий у линтера <c>HelpCheck</c>
/// и у индекса API: что проверено, то и ищется
/// </summary>
/// <param name="Name">Имя файла без <c>.md</c> — <c>balance</c>, <c>notes</c></param>
/// <param name="Title">Название из фронтматтера, первая часть «хлебных крошек» (6)</param>
/// <param name="Page">Маршрут приложения, куда ассистент может отправить ссылкой</param>
/// <param name="Problems">
/// Что разбор не смог разложить: нет фронтматтера, текст вне разделов. Индекс такую
/// статью всё равно берёт как есть — до прода она не доезжает, её останавливает линтер
/// </param>
public record HelpArticle(
    string Name,
    string Title,
    string? Page,
    IReadOnlyList<HelpSection> Sections,
    IReadOnlyList<string> Problems);

/// <summary>Раздел <c>##</c> — один вопрос и один чанк индекса (3.3)</summary>
/// <param name="Title">Заголовок раздела — как человек бы спросил</param>
/// <param name="Body">Тело без строки «Как спрашивают:»; <c>###</c> внутри — его часть</param>
/// <param name="Asks">Строка «Как спрашивают:» без самого префикса; <c>null</c> — её нет</param>
/// <param name="Line">Номер строки заголовка в файле — для сообщений линтера</param>
public record HelpSection(string Title, string Body, string? Asks, int Line)
{
    /// <summary>
    /// Текст чанка (6): «хлебные крошки», тело и «Как спрашивают». Крошки дают
    /// короткому разделу контекст целой статьи — без них «нажмите „Закрыть период“»
    /// не знает, что речь про кошельки
    /// </summary>
    public string ChunkText(string articleTitle) =>
        Asks == null
            ? $"{articleTitle} › {Title}\n\n{Body}"
            : $"{articleTitle} › {Title}\n\n{Body}\n\n{HelpArticleParser.AsksPrefix} {Asks}";
}
