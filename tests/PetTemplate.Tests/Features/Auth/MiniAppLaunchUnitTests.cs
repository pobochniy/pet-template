using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Services.Interfaces;
using PetTemplate.Services.Services;
using PetTemplate.Shared.Options;
using PetTemplate.Tests.EntityBuilders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace PetTemplate.Tests.Features.Auth;

/// <summary>
/// Подпись и свежесть параметров запуска mini-app (спец. accounts, 5.6). Юнит-тестом,
/// а не через API: тестовый хост работает в Development, где подпись Telegram не
/// проверяется — проверять здесь нужно именно боевой путь
/// </summary>
public class MiniAppLaunchUnitTests
{
    private const string BotToken = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";

    private static MiniAppLaunch Launch(bool vkSignValid = true)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Telegram:BotToken"] = BotToken })
            .Build();
        var vk = new Mock<IVkService>();
        vk.Setup(x => x.IsSignValid(It.IsAny<string>())).Returns(vkSignValid);
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns("Release");

        return new MiniAppLaunch(
            new TelegramService(configuration),
            vk.Object,
            Options.Create(new AccountOptions()),
            environment.Object,
            NullLogger<MiniAppLaunch>.Instance);
    }

    private static string VkLaunch(DateTimeOffset signedAt) =>
        $"?vk_app_id=51797414&vk_language=en&vk_platform=desktop_web&vk_ref=other&vk_ts={signedAt.ToUnixTimeSeconds()}" +
        "&vk_user_id=2308481&sign=test_sign";

    [Fact]
    public void AcceptsFreshTelegramSignature()
    {
        var initData = new TelegramInitDataBuilder().WithUserId(42).WithBotToken(BotToken).Please();

        var user = Launch().Telegram(initData);

        Assert.Equal(42, user!.Id);
    }

    [Fact]
    public void RejectsStaleTelegramSignature()
    {
        var initData = new TelegramInitDataBuilder()
            .WithBotToken(BotToken)
            .SignedAt(DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(-1))
            .Please();

        Assert.Null(Launch().Telegram(initData));
    }

    [Fact]
    public void RejectsForeignTelegramSignature()
    {
        var initData = new TelegramInitDataBuilder().WithBotToken("999:other-bot").Please();

        Assert.Null(Launch().Telegram(initData));
    }

    /// <summary>Строка — как <c>location.search</c>, с ведущим <c>?</c></summary>
    [Fact]
    public void AcceptsFreshVkLaunch()
    {
        var user = Launch().Vk(VkLaunch(DateTimeOffset.UtcNow.AddHours(-23)));

        Assert.Equal(2308481, user!.Id);
        Assert.Equal("en", user.Language);
    }

    [Fact]
    public void RejectsStaleVkLaunch() =>
        Assert.Null(Launch().Vk(VkLaunch(DateTimeOffset.UtcNow.AddHours(-25))));

    [Fact]
    public void RejectsBadVkSign() =>
        Assert.Null(Launch(vkSignValid: false).Vk(VkLaunch(DateTimeOffset.UtcNow)));
}
