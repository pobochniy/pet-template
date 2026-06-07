using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto;

namespace PetTemplate.Services.Services;

public class ProfileService(ApplicationContext db)
{
    public async Task Save(ProfileDto dto)
    {
        var entity = await db.Profiles
            .SingleAsync(x => x.Id == dto.Id);

        if (!string.IsNullOrWhiteSpace(dto.UserName)) entity.UserName = dto.UserName;
        entity.Comment = dto.Comment;
        entity.IsAdult = dto.IsAdult;
        entity.HasAcceptedTerms = dto.HasAcceptedTerms;
        
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException mysqlEx && 
                                          mysqlEx.Number == 1062 && 
                                          mysqlEx.Message.Contains("IX_Profiles_UserName"))
        {
            // Дублирование UserName - добавляем GUID и обрезаем до 20 символов
            var guid = Guid.NewGuid().ToString("N")[..8]; // Берем первые 8 символов GUID
            var maxBaseLength = 20 - 1 - guid.Length; // 20 - underscore - guid length
            var baseName = dto.UserName!.Length > maxBaseLength 
                ? dto.UserName[..maxBaseLength] 
                : dto.UserName;
            entity.UserName = $"{baseName}_{guid}";
            await db.SaveChangesAsync();
        }
    }

    public async Task SetAvatar(Guid userId, int avatarId)
    {
        var entity = await db.Profiles
            .SingleAsync(x => x.Id == userId);

        entity.AvatarId = avatarId;
        
        await db.SaveChangesAsync();
    }

    public async Task<Profile> Get(Guid userId, CancellationToken ct)
    {
        return await db.Profiles.SingleAsync(x => x.Id == userId, ct);
    }

    public async Task ConnectWallet(Guid userId, string walletAddress, CancellationToken ct)
    {
        var entity = await db.Profiles.SingleAsync(x => x.Id == userId, ct);
        entity.WalletAddress = walletAddress;
        entity.WalletConnectedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DisconnectWallet(Guid userId, CancellationToken ct)
    {
        var entity = await db.Profiles.SingleAsync(x => x.Id == userId, ct);
        entity.WalletAddress = null;
        entity.WalletConnectedAt = null;
        await db.SaveChangesAsync(ct);
    }

}