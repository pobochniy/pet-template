using System.Text;
using PetTemplate.Services.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PetTemplate.Api.Middlewares;
using PetTemplate.Db.Entity;

namespace PetTemplate.Api.Configs;

public static class ServicesExtensions
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services, IConfiguration config)
    {
        services
            .AddAppOptions(config)
            .ConfigureCors()
            .AddAuthentication(config)
            .AddControllersWithSwagger()
            // .RegisterMiddlewares()
            .AddDbContext(config)
            .AddAppServices();

        return services;
    }

    private static IServiceCollection AddAppOptions(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<JwtOptions>().Bind(config.GetSection("JwtOptions"));

        return services;
    }

    private static IServiceCollection AddAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var jwtOptions = config.GetSection("JwtOptions").Get<JwtOptions>();

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOptions?.Issuer,
                    ValidAudience = jwtOptions?.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions?.SigningKey ?? ""))
                };
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o =>
            {
                o.Cookie.HttpOnly = true;
                o.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = redirectContext =>
                    {
                        redirectContext.HttpContext.Response.StatusCode = 401;
                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }

    private static IServiceCollection AddControllersWithSwagger(this IServiceCollection services)
    {
        services
            .AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        return services;
    }

    private static IServiceCollection ConfigureCors(this IServiceCollection services)
    {
        services.AddCors(c => c.AddPolicy("AllowSpecificOrigin", corsBuilder =>
        {
            corsBuilder
                .WithOrigins("http://localhost:4200")
                .AllowCredentials()
                .AllowAnyMethod()
                .AllowAnyHeader();
        }));

        return services;
    }
    
    private static IServiceCollection AddDbContext(this IServiceCollection services, IConfiguration config)
    {
        var connString = config.GetConnectionString("AppConnection");
        var serverVersion = new MySqlServerVersion(new Version(8, 0, 28));
        services.AddDbContext<ApplicationContext>(opt => opt.UseMySql(
            connString!,
            serverVersion,
            x => x.MigrationsAssembly("PetTemplate.Db")
        ));

        return services;
    }
    
    
    private static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddTransient<PermissionsValidation>();
        services.AddTransient<AuthService>();
        services.AddTransient<ProfileService>();
        services.AddTransient<TelegramService>();

        return services;
    }
}