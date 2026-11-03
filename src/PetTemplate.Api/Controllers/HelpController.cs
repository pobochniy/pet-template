using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetTemplate.Services.Help;
using PetTemplate.Shared.Dto.Help;

namespace PetTemplate.Api.Controllers;

/// <summary>
/// Справка на сайте (спец. help, 6.5): список статей и статья целиком — из того же выпуска
/// в памяти, по которому ищет ассистент. Анонимно: в справке только то, что можно выложить
/// на публичную страницу помощи (4.1), и читать её нужно и до входа. Справки ещё нет
/// (выпуск не опубликован) — пустой список
/// </summary>
[Route("api/[controller]")]
[AllowAnonymous]
public class HelpController(HelpSearch help) : ControllerBase
{
    [HttpGet]
    [Produces(typeof(HelpArticleSummaryDto[]))]
    public IActionResult List()
    {
        var pages = help.Current?.Pages ?? [];

        return Ok(pages.Select(x => new HelpArticleSummaryDto { Slug = x.Slug, Title = x.Title }).ToArray());
    }

    [HttpGet("{slug}")]
    [Produces(typeof(HelpArticleDto))]
    public IActionResult Article(string slug)
    {
        var page = help.Current?.Pages.SingleOrDefault(x => x.Slug == slug);
        if (page == null) return NotFound();

        return Ok(new HelpArticleDto { Slug = page.Slug, Title = page.Title, Markdown = page.Markdown });
    }
}
