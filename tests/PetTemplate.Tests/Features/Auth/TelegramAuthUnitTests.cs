using PetTemplate.Services.Services;
using Microsoft.Extensions.Configuration;

namespace PetTemplate.Tests.Features.Auth;

public class TelegramAuthUnitTests
{
    [Fact]
    public void CanAuthFromTelegramTest()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string>
        {
            { "Telegram:BotToken", "8787652196:AAGivniuYQlgDR-tqMjGMWvZs6AVSGSRvYw" },
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings!)
            .Build();
        
        var telegramService = new TelegramService(configuration);
        
        const string initData = "user=%7B%22id%22%3A936951059%2C%22first_name%22%3A%22%F0%9F%A7%99%F0%9F%8F%BC%E2%80%8D%E2%99%82%EF%B8%8F%20%F0%9D%95%81%F0%9D%95%96%F0%9D%95%96%F0%9D%95%A3%F0%9D%95%A0%F0%9D%95%9C%22%2C%22last_name%22%3A%22%22%2C%22username%22%3A%22pobochniy%22%2C%22language_code%22%3A%22ru%22%2C%22allows_write_to_pm%22%3Atrue%2C%22photo_url%22%3A%22https%3A%5C%2F%5C%2Ft.me%5C%2Fi%5C%2Fuserpic%5C%2F320%5C%2Ftj7hTrCs-eCxs96sMbOTIPwioXe6Jfx8Tou5pMyx5rQ.svg%22%7D&chat_instance=5297033871833558844&chat_type=private&auth_date=1772802195&signature=uNdCvTg146H5j93Dg6mEX1_iBM8qq2l2N0k5UEy3uKU__RhwFSEJOps0v8NdYD2uxvkKFNR6iJKosiRPYZscDQ&hash=c338285b0844bf96e444a3a3cb372282eae84fe1f2535d48c701cf56b4b73ac4";
        
        // Act
        var isValid = telegramService.ValidateInitData(initData);

        // Assert
        Assert.True(isValid);
    }

    private const string BotToken = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";

    private static TelegramService Service(string botToken) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Telegram:BotToken"] = botToken })
        .Build());

    /// <summary>
    /// Пустой токен бота — отказ, как у VK (<c>VkService.IsSignValid</c>): подпись ключом из
    /// пустой строки посчитает кто угодно
    /// </summary>
    [Fact]
    public void RejectsAnySignatureWithoutBotToken()
    {
        var forged = new EntityBuilders.TelegramInitDataBuilder().WithBotToken("").Please();

        Assert.False(Service("").ValidateInitData(forged));
    }

    [Fact]
    public void AcceptsOwnSignature()
    {
        var initData = new EntityBuilders.TelegramInitDataBuilder().WithBotToken(BotToken).Please();

        Assert.True(Service(BotToken).ValidateInitData(initData));
    }

    [Fact]
    public void RejectsForeignSignature()
    {
        var initData = new EntityBuilders.TelegramInitDataBuilder().WithBotToken("654321:foreign-bot").Please();

        Assert.False(Service(BotToken).ValidateInitData(initData));
    }

    /// <summary>Подпись не в шестнадцатеричном виде или не той длины — отказ, а не исключение</summary>
    [Theory]
    [InlineData("zz")]
    [InlineData("abcd")]
    public void RejectsMalformedHash(string hash)
    {
        Assert.False(Service(BotToken).ValidateInitData($"auth_date=1&user=%7B%22id%22%3A1%7D&hash={hash}"));
    }
}
