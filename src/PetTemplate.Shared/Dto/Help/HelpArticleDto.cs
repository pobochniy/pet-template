namespace PetTemplate.Shared.Dto.Help;

/// <summary>Статья в списке справки (спец. help, 6.5): имя для адреса и название</summary>
public class HelpArticleSummaryDto
{
    /// <summary>Имя файла статьи без <c>.md</c> — часть адреса <c>/help/{slug}</c></summary>
    public required string Slug { get; init; }

    public required string Title { get; init; }
}

/// <summary>
/// Статья целиком (спец. help, 6.5): разделы <c>##</c> markdown-текстом, без фронтматтера
/// и без строк «Как спрашивают» — они только для поиска
/// </summary>
public class HelpArticleDto
{
    public required string Slug { get; init; }

    public required string Title { get; init; }

    public required string Markdown { get; init; }
}
