namespace PetTemplate.Shared.Enums;

/// <summary>
/// Площадка входа (спец. accounts, раздел 3): у аккаунта не больше одного Telegram и
/// одного VK. Копия на клиенте — <c>shared/enums/external-provider.enum.ts</c>,
/// значения только в конец
/// </summary>
public enum ExternalProvider
{
    Telegram = 1,
    Vk = 2
}
