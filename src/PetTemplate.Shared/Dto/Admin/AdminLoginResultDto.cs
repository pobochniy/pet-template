namespace PetTemplate.Shared.Dto.Admin;

/// <summary>
/// Ответ <c>POST Admin/Login</c>: токен своей схемы и его срок — по нему клиент
/// сам уводит на вход, когда час истёк (продления нет, спец. admin, 3.3)
/// </summary>
public class AdminLoginResultDto
{
    public string Token { get; set; } = null!;

    public DateTime ExpiresUtc { get; set; }

    public AdminDto Admin { get; set; } = null!;
}
