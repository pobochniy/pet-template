namespace PetTemplate.Services.Interfaces;

/// <summary>Подпись параметров запуска mini-app VK (спец. accounts, 5.6)</summary>
public interface IVkService
{
    bool IsSignValid(string queryString);
}
