namespace PetTemplate.Shared.Dto.Admin;

/// <summary>
/// «Сводка» — единственный экран каркаса админки (спец. admin, 6): какая сборка API
/// сейчас отвечает и который час на сервере
/// </summary>
public class AdminSummaryDto
{
    /// <summary>Версия сборки API — по ней видно, докатился ли выкат</summary>
    public string Version { get; set; } = string.Empty;

    public DateTime ServerTimeUtc { get; set; }
}
