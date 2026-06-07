using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto;

namespace PetTemplate.Services.Services;

public class AuthService(ApplicationContext db)
{
    // public async Task Register(RegisterDto dto)
    // {
    //     if (await db.Profiles.AnyAsync(x => x.UserName == dto.UserName))
    //     {
    //         throw new ConstraintException("UserName");
    //     }
    //
    //     if (!string.IsNullOrWhiteSpace(dto.Email) && await db.Profiles.AnyAsync(x => x.Email == dto.Email))
    //     {
    //         throw new ConstraintException("Email");
    //     }
    //
    //     if (!string.IsNullOrWhiteSpace(dto.Phone) && await db.Profiles.AnyAsync(x => x.PhoneNumber == dto.Phone))
    //     {
    //         throw new ConstraintException("Phone");
    //     }
    //
    //     var salt = Guid.NewGuid();
    //
    //     var profile = new Profile
    //     {
    //         Id = Guid.NewGuid(),
    //         UserName = dto.UserName,
    //         Email = dto.Email,
    //         PhoneNumber = dto.Phone,
    //         User = new User
    //         {
    //             SecurityStamp = salt.ToString(),
    //             PasswordHash = GetHashString($"{salt}#{dto.Password}")
    //         }
    //     };
    //
    //     await db.Profiles.AddAsync(profile);
    //     await db.SaveChangesAsync();
    //
    //     if (db.Profiles.Count() == 1)
    //     {
    //         var allRoles = Enum.GetValues(typeof(RoleEnum))
    //             .Cast<RoleEnum>()
    //             .Select(x => new UserInRole
    //             {
    //                 UserId = profile.Id,
    //                 RoleId = x
    //             })
    //             .Where(x => x.RoleId != 0)
    //             .ToArray();
    //
    //         await db.UserInRoles.AddRangeAsync(allRoles);
    //         await db.SaveChangesAsync();
    //     }
    // }
    //
    // public async Task<UserDto> LogIn(LoginDto dto)
    // {
    //     Profile profile = null;
    //
    //     if (dto.IsPhone)
    //     {
    //         profile = await db.Profiles.Include(x => x.User).SingleOrDefaultAsync(x => x.PhoneNumber == dto.Login);
    //     }
    //     else if (dto.IsEmail)
    //     {
    //         profile = await db.Profiles.Include(x => x.User).SingleOrDefaultAsync(x => x.Email == dto.Login);
    //     }
    //     else
    //     {
    //         profile = await db.Profiles.Include(x => x.User).SingleOrDefaultAsync(x => x.UserName == dto.Login);
    //     }
    //
    //     if (profile == null || profile.User.PasswordHash !=
    //         GetHashString($"{profile.User.SecurityStamp}#{dto.Password}"))
    //         throw new UnauthorizedAccessException();
    //
    //     var res = new UserDto
    //     {
    //         Id = profile.Id,
    //         UserName = profile.UserName,
    //         Email = profile.Email,
    //         Phone = profile.PhoneNumber,
    //         Roles = await db.UserInRoles
    //             .Where(x => x.UserId == profile.Id)
    //             .Select(x => x.RoleId)
    //     };
    //
    //     return res;
    // }

    public async Task<UserDto> GetOrRegisterUser(long telegramId, string? userName = null)
    {
        var user = await db.Users
            .Include(x => x.Profile)
            .SingleOrDefaultAsync(x => x.TelegramId == telegramId);

        if (user == null)
        {
            var originalUserName = userName ?? $"user_{telegramId}";
            var profile = new Profile
            {
                Id = Guid.NewGuid(),
                UserName = originalUserName,
                AvatarId = Random.Shared.Next(0, 8),
                User = new User
                {
                    TelegramId = telegramId
                }
            };

            await db.Profiles.AddAsync(profile);
            
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
                var baseName = originalUserName.Length > maxBaseLength 
                    ? originalUserName[..maxBaseLength] 
                    : originalUserName;
                profile.UserName = $"{baseName}_{guid}";
                await db.SaveChangesAsync();
            }

            user = await db.Users
                .Include(x => x.Profile)
                .SingleOrDefaultAsync(x => x.TelegramId == telegramId);
        }

        var res = new UserDto
        {
            Id = user!.Id,
            Profile = new ProfileDto
            {
                Id = user.Profile!.Id,
                UserName = user.Profile.UserName,
                AvatarId = user.Profile.AvatarId,
                Comment = user.Profile.Comment,
                IsAdult = user.Profile.IsAdult,
                HasAcceptedTerms = user.Profile.HasAcceptedTerms,
                WalletAddress = user.Profile.WalletAddress
            },
            Permissions = await db.UserInPermissions
                .Where(x => x.UserId == user.Id)
                .Select(x => x.PermissionId)
                .ToArrayAsync()
        };

        return res;
    }
    // private static string GetHashString(string inputString)
    // {
    //     var sb = new StringBuilder();
    //     foreach (var b in GetHash(inputString))
    //         sb.Append(b.ToString("X2"));
    //
    //     return sb.ToString();
    // }
    //
    // private static byte[] GetHash(string inputString)
    // {
    //     return SHA256.HashData(Encoding.UTF8.GetBytes(inputString));
    // }
}