namespace PetTemplate.Services.Interfaces;

public interface IVkService
{
    bool IsSignValid(string queryString);

    // Task<(long vkId, string token)?> GetAuthToken(Guid userId);

    // Task SetToken(Guid userId, string token);
}