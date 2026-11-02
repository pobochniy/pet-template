using Microsoft.Extensions.Options;
using PetTemplate.Services.Options;
using PetTemplate.Services.Services;

namespace PetTemplate.Tests.Features.Auth;

public class VkAuthUnitTests
{
    private const string Signed =
        "vk_access_token_settings=&vk_app_id=51797414&vk_are_notifications_enabled=0&vk_is_app_user=1&vk_is_favorite=0&vk_language=ru&vk_platform=desktop_web&vk_ref=other&vk_ts=1700592895&vk_user_id=2308481&sign=cG44Gue7ebRa5X_ba5enn5xT4LbCvgAU6TpbfO3xSjY";

    private static VkService Sut(string privateKey = "inPLLzvReAlbvyH9HnbD") =>
        new(Options.Create(new VkOptions { AppId = 51797414, PrivateKey = privateKey }));

    /// <summary>Подписаны только <c>vk_*</c>: прочее, что VK или фрейм допишут к адресу, подпись не ломает</summary>
    [Fact]
    public void IgnoresNonVkParams() =>
        Assert.True(Sut().IsSignValid("odr_enabled=1&" + Signed + "&utm_source=catalog"));

    [Fact]
    public void RejectsTamperedUserId() =>
        Assert.False(Sut().IsSignValid(Signed.Replace("vk_user_id=2308481", "vk_user_id=2308482")));

    /// <summary>Ключ не задан на сервере — вход из mini-app VK закрыт, а не открыт всем</summary>
    [Fact]
    public void RejectsEverythingWithoutKey() =>
        Assert.False(Sut(privateKey: "").IsSignValid(Signed));

    [Theory]
    [InlineData("vk_access_token_settings=friends,menu&vk_app_id=51797414&vk_are_notifications_enabled=0&vk_is_app_user=1&vk_is_favorite=1&vk_language=ru&vk_platform=desktop_web&vk_ref=other&vk_ts=1701528645&vk_user_id=2308481&sign=SSm-1iu9Vooelj9UPPx092aJdXSOo-OMHWrJNDXAmfc")]
    [InlineData("vk_access_token_settings=&vk_app_id=51797414&vk_are_notifications_enabled=0&vk_is_app_user=1&vk_is_favorite=0&vk_language=ru&vk_platform=desktop_web&vk_ref=other&vk_ts=1700592895&vk_user_id=2308481&sign=cG44Gue7ebRa5X_ba5enn5xT4LbCvgAU6TpbfO3xSjY")]
    [InlineData("vk_access_token_settings=friends,menu&vk_app_id=51797414&vk_are_notifications_enabled=0&vk_is_app_user=1&vk_is_favorite=1&vk_language=ru&vk_platform=desktop_web&vk_ref=other&vk_ts=1701602428&vk_user_id=2308481&sign=kTYiOvMXifhFwAt1zgVYFWOg60XYs5TS14gE8HTymH0")]
    [InlineData("vk_access_token_settings=friends,menu&vk_app_id=51797414&vk_are_notifications_enabled=0&vk_is_app_user=1&vk_is_favorite=1&vk_language=ru&vk_platform=desktop_web&vk_ref=bookmarks_all_section&vk_ts=1702458329&vk_user_id=2308481&sign=mg6FfZYNX9g8u85psAFTWeT1Ab9ca9VMoId74TKKiRI")]
    public void WhenCorrectQueryStringThenValidSign(string queryString)
    {
        var vkOptions = Options.Create(new VkOptions
        {
            AppId = 51797414,
            PrivateKey = "inPLLzvReAlbvyH9HnbD"
        });
        var sut = new VkService(vkOptions);

        var isSignValid = sut.IsSignValid(queryString);

        Assert.True(isSignValid);
    }
}
