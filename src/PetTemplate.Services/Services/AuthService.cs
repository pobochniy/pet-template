using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;

namespace PetTemplate.Services.Services;

public class AuthService(ApplicationContext db)
{
    public async Task Register(RegisterDto dto, CancellationToken ct)
        {
            if (await db.Profiles.AnyAsync(x => x.UserName == dto.UserName, cancellationToken: ct))
            {
                throw new ConstraintException("UserName");
            }

            if (!string.IsNullOrWhiteSpace(dto.Email) && await db.Profiles.AnyAsync(x => x.Email == dto.Email, cancellationToken: ct))
            {
                throw new ConstraintException("Email");
            }

            if (!string.IsNullOrWhiteSpace(dto.Phone) && await db.Profiles.AnyAsync(x => x.PhoneNumber == dto.Phone, cancellationToken: ct))
            {
                throw new ConstraintException("Phone");
            }

            var salt = Guid.NewGuid();

            var profile = new Profile
            {
                Id = Guid.NewGuid(),
                AvatarId = Random.Shared.Next(0, 8),
                UserName = dto.UserName,
                Email = dto.Email,
                PhoneNumber = dto.Phone,
                User = new User
                {
                    SecurityStamp = salt.ToString(),
                    PasswordHash = GetHashString($"{salt}#{dto.Password}")
                }
            };

            await db.Profiles.AddAsync(profile, ct);
            await db.SaveChangesAsync(ct);

            if (await db.Profiles.CountAsync(ct) == 1)
            {
                var allRoles = Enum.GetValues(typeof(PermissionEnum))
                    .Cast<PermissionEnum>()
                    .Select(x => new UserInPermission
                    {
                        UserId = profile.Id,
                        PermissionId = x
                    })
                    .Where(x => x.PermissionId != 0)
                    .ToArray();

                await db.UserInPermissions.AddRangeAsync(allRoles, ct);
                await db.SaveChangesAsync(ct);
            }
        }

        public async Task<UserDto> LogIn(LoginDto dto, CancellationToken ct)
        {
            Profile? profile = null;

            if (dto.IsPhone)
            {
                profile = await db.Profiles.Include(x => x.User).SingleOrDefaultAsync(x => x.PhoneNumber == dto.Login, cancellationToken: ct);
            }
            else if (dto.IsEmail)
            {
                profile = await db.Profiles.Include(x => x.User).SingleOrDefaultAsync(x => x.Email == dto.Login, cancellationToken: ct);
            }
            else
            {
                profile = await db.Profiles.Include(x => x.User).SingleOrDefaultAsync(x => x.UserName == dto.Login, cancellationToken: ct);
            }

            if (profile == null || profile.User!.PasswordHash !=
                GetHashString($"{profile.User.SecurityStamp}#{dto.Password}"))
                throw new UnauthorizedAccessException();

            var res = new UserDto
            {
                Id = profile.User!.Id,
                Profile = new ProfileDto
                {
                    Id = profile.User.Profile!.Id,
                    UserName = profile.User.Profile.UserName,
                    AvatarId = profile.User.Profile.AvatarId,
                    Email = profile.User.Profile.Email,
                    Phone = profile.User.Profile.PhoneNumber,
                    Comment = profile.User.Profile.Comment,
                    IsAdult = profile.User.Profile.IsAdult,
                    HasAcceptedTerms = profile.User.Profile.HasAcceptedTerms,
                    WalletAddress = profile.User.Profile.WalletAddress
                },
                Permissions = await db.UserInPermissions
                    .Where(x => x.UserId == profile.User.Id)
                    .Select(x => x.PermissionId)
                    .ToArrayAsync(cancellationToken: ct)
            };

            return res;
        }

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
    
    public async Task<UserDto> GetOrRegisterUserForVk(long vkUserId, string? token)
    {
        var user = await db.Users
            .Include(x => x.Profile)
            .SingleOrDefaultAsync(x => x.VkId == vkUserId);
            
        if (user != null && !string.IsNullOrWhiteSpace(token))
        {
            user.VkAuthToken = token;
            await db.SaveChangesAsync();
        }
            
        if (user == null)
        {
            var profile = new Profile
            {
                Id = Guid.NewGuid(),
                // UserName = dto.UserName,
                // Email = dto.Email,
                // PhoneNumber = dto.Phone,
                User = new User
                {
                    VkId = vkUserId,
                    VkAuthToken = token
                    // SecurityStamp = salt.ToString(),
                    // PasswordHash = GetHashString($"{salt}#{dto.Password}")
                }
            };


            await db.Profiles.AddAsync(profile);
            await db.SaveChangesAsync();
                
            user = await db.Users
                .Include(x => x.Profile)
                .SingleOrDefaultAsync(x => x.VkId == vkUserId);
        }
            
        var res = new UserDto
        {
            Id = user!.Id,
            Profile = new ProfileDto
            {
                Id = user.Profile!.Id,
                UserName = user.Profile.UserName,
                AvatarId = user.Profile.AvatarId,
                Email = user.Profile.Email,
                Phone = user.Profile.PhoneNumber,
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

    
    private static string GetHashString(string inputString)
    {
        var sb = new StringBuilder();
        foreach (var b in GetHash(inputString))
            sb.Append(b.ToString("X2"));
    
        return sb.ToString();
    }
    
    private static byte[] GetHash(string inputString)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(inputString));
    }
}