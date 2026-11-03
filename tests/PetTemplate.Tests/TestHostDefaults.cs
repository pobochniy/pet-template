using System.Runtime.CompilerServices;

namespace PetTemplate.Tests;

/// <summary>
/// Настройки процесса тестов, которые обязаны встать до того, как поднимется
/// первый хост: <c>WebApplication.CreateBuilder</c> читает их в самом начале
/// <c>Program</c>, а туда ни <c>WebApplicationFactory</c>, ни <c>UseSetting</c>
/// уже не дотягиваются
/// </summary>
internal static class TestHostDefaults
{
    /// <summary>
    /// Каждый тест поднимает свой хост, и каждый хост по умолчанию вешает на
    /// <c>appsettings*.json</c> слежение за изменениями: на Linux это отдельный
    /// inotify instance (лимит <c>fs.inotify.max_user_instances</c> — 128), на macOS —
    /// FSEvents, не больше 512 на процесс. Хосты останавливаются после теста
    /// (<see cref="TestHosts"/>), но параллельные классы держат их одновременно, и
    /// упираться в лимит незачем: конфигурация в тестах на ходу не меняется
    /// </summary>
    [ModuleInitializer]
    internal static void DisableConfigFileWatching()
    {
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");

        // Подстраховка: если какой-то провайдер всё же запросит слежение, оно
        // пойдёт опросом по таймеру, а не через inotify
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");
    }
}
