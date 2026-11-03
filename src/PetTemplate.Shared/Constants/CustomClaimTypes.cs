namespace PetTemplate.Shared.Constants;

public static class CustomClaimTypes
{
    /// <summary>
    /// Админская роль — числом <c>AdminRole</c>, по клейму на роль (спец. admin, 3.3).
    /// Живёт только в токене админской схемы: в токене продукта его не бывает
    /// </summary>
    public const string AdminRole = "admin_role";
}
