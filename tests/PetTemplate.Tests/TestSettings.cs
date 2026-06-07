namespace PetTemplate.Tests;

public static class TestSettings
{
    // Telegram Bot Token - используется для валидации InitData от Telegram Mini App
    public const string DefaultTelegramBotToken = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11";
    
    public static Action<IDictionary<string, string?>> ConfigureAuth(string? telegramBotToken = null)
    {
        return settings =>
        {
            settings["Telegram:BotToken"] = telegramBotToken ?? DefaultTelegramBotToken;
            settings["JwtOptions:Issuer"] = "TestIssuer";
            settings["JwtOptions:Audience"] = "TestAudience";
            settings["JwtOptions:SigningKey"] = "TestSigningKeyThatIsLongEnough123456";
            settings["JwtOptions:ExpirationSeconds"] = "3600";
        };
    }
}
