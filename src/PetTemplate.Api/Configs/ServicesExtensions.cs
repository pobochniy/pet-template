using System.Security.Claims;
using System.Text;
using PetTemplate.Services.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using PetTemplate.Api.Extensions.Auth;
using PetTemplate.Api.Middlewares;
using PetTemplate.Api.Workers;
using PetTemplate.Db.Entity;
using PetTemplate.Services.Admin;
using PetTemplate.Services.Assistant;
using PetTemplate.Services.Assistant.Hooks;
using PetTemplate.Services.Assistant.Models;
using PetTemplate.Services.Assistant.Plans;
using PetTemplate.Services.Assistant.Tools;
using PetTemplate.Services.Assistant.Transcription;
using PetTemplate.Services.Billing;
using PetTemplate.Services.Commands;
using PetTemplate.Services.Interfaces;
using PetTemplate.Services.LocalQueueHandlers;
using PetTemplate.Services.Mail;
using PetTemplate.Services.Options;
using PetTemplate.Services.Help;
using PetTemplate.Services.Qdrant;
using PetTemplate.Services.Qdrant.Migrations;
using PetTemplate.Services.Search;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Errors;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Options;

namespace PetTemplate.Api.Configs;

public static class ServicesExtensions
{
    /// <summary>Именованный клиент расшифровки голоса — свой таймаут, адрес ставится на запросе</summary>
    public const string TranscriptionClient = "transcription";

    /// <summary>Имя приложения в кольце Data Protection — общее у всех экземпляров и выкаток</summary>
    public const string DataProtectionApplicationName = "PetTemplate";

    public static IServiceCollection ConfigureServices(this IServiceCollection services, IConfiguration config)
    {
        services
            .AddAppOptions(config)
            .ConfigureCors()
            .ConfigureForwardedHeaders()
            .AddAuthentication(config)
            .AddControllersWithSwagger()
            // .RegisterMiddlewares()
            .AddDbContext(config)
            .AddKeyRing()
            .AddAppServices()
            .AddSearch(config)
            .AddAssistant()
            .AddCommandQueue(config);

        return services;
    }

    private static IServiceCollection AddAppOptions(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<JwtOptions>().Bind(config.GetSection("JwtOptions"));
        services.AddOptions<VkOptions>().Bind(config.GetSection("VkOptions"));
        services.AddOptions<TelegramLoginOptions>().Bind(config.GetSection("TelegramLoginOptions"));
        services.AddOptions<VkIdOptions>().Bind(config.GetSection("VkIdOptions"));
        services.AddOptions<AccountOptions>().Bind(config.GetSection("AccountOptions"));
        services.AddOptions<MailOptions>().Bind(config.GetSection("MailOptions"));
        services.AddOptions<AdminOptions>().Bind(config.GetSection("AdminOptions"));
        services.AddOptions<QdrantOptions>().Bind(config.GetSection("QdrantOptions"));
        services.AddOptions<SearchOptions>().Bind(config.GetSection("SearchOptions"));
        services.AddOptions<HelpOptions>().Bind(config.GetSection("HelpOptions"));
        services.AddOptions<AssistantOptions>().Bind(config.GetSection("AssistantOptions"));
        services.AddOptions<ModelBreakerOptions>().Bind(config.GetSection("ModelBreakerOptions"));
        services.AddOptions<SpendOptions>().Bind(config.GetSection("SpendOptions"));

        return services;
    }

    private static IServiceCollection AddAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var jwtOptions = config.GetSection("JwtOptions").Get<JwtOptions>();
        var adminOptions = config.GetSection("AdminOptions").Get<AdminOptions>() ?? new AdminOptions();
        EnsureProductKeyIsUsable(jwtOptions);
        EnsureAdminSchemeIsSeparate(jwtOptions, adminOptions);

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
                options.Events = new JwtBearerEvents { OnTokenValidated = RejectRevokedSession };
            })
            // Админская схема (спец. admin, 3.3): своя аудитория и свой ключ, поэтому
            // токен продукта её не проходит, а её токен не проходит схему продукта.
            // По умолчанию не выбирается — спрашивает её только [AuthorizeAdmin].
            // Запас по часам нулевой: час жизни и есть час, продления нет.
            // Отзыв сессий — тот же, что у продукта (спец. accounts, 8.1 и 10): в админку входят
            // паролем продукта, и его смена, как и удаление аккаунта, гасят и админский токен.
            // Сверх того роли токена урезаются до тех, что ещё в базе (спец. admin, 3.3)
            .AddJwtBearer(AdminAuth.Scheme, options =>
            {
                options.TokenValidationParameters = AdminTokenValidation(adminOptions);
                options.Events = new JwtBearerEvents { OnTokenValidated = RejectRevokedAdminSession };
            })
            // Та же проверка токена, но из куки шлюза (спец. admin, 3.3) — и только
            // из неё, как и кука не читается схемой выше. Без куки — NoResult, а не пустой
            // Token: на пустой обработчик сам пошёл бы в заголовок Authorization
            .AddJwtBearer(AdminAuth.GatewayScheme, options =>
            {
                options.TokenValidationParameters = AdminTokenValidation(adminOptions);
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Cookies[adminOptions.GatewayCookieName];
                        if (string.IsNullOrEmpty(token))
                            context.NoResult();
                        else
                            context.Token = token;
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = RejectRevokedAdminSession
                };
            });

        services.AddMemoryCache();
        services.AddScoped<SessionGuard>();
        services.AddTransient<AdminTokenFactory>();
        // Счётчики неудачных паролей живут в памяти процесса — один на всё приложение
        // (API один, tech-debt/single-instance.md):
        // общий для входа в админку и проверок пароля в продукте (спец. admin, 3.3; accounts, 8.1)
        services.AddSingleton<LoginLimiter>();

        // Входы площадок (спец. accounts, 5.2, 5.3, 5.6): Telegram на сайте проверяется копией
        // ключей без сети, VK ID — обменом кода; HTTP-клиент VK — из фабрики, тесты подменяют
        // его обработчик. Mini-app — подписью параметров запуска (`MiniAppLaunch`)
        services.AddTransient<TelegramLogin>();
        services.AddSingleton<TelegramTokenReplay>();
        // Таймаут свой, а не 100 секунд по умолчанию: на входе человек ждёт у кнопки,
        // а зависший запрос держит поток и соединение
        services.AddHttpClient<VkId>(x => x.Timeout = TimeSpan.FromSeconds(10));
        services.AddTransient<MiniAppLaunch>();
        services.AddTransient<ExternalProof>();

        return services;
    }

    /// <summary>
    /// Подпись и срок — ещё не всё (спец. accounts, 8.1 и 10): токен удалённого аккаунта
    /// и токен, выпущенный до смены пароля, отбиваются здесь же — 401, как просроченный.
    /// Одно место на обе схемы: продукт и админку
    /// </summary>
    private static async Task RejectRevokedSession(TokenValidatedContext context)
    {
        var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var guard = context.HttpContext.RequestServices.GetRequiredService<SessionGuard>();

        if (!Guid.TryParse(userId, out var id)
            || !await guard.IsValid(id, IssuedAt.Of(context.Principal!), context.HttpContext.RequestAborted))
        {
            context.Fail("Session revoked");
        }
    }

    /// <summary>
    /// Админский токен — то же, что выше, и ещё роли (спец. admin, 3.3): из токена остаются
    /// только те, что ещё лежат в базе. Иначе снятая роль жила бы в токене до конца его часа,
    /// и удалённый из админки с ролью <c>Accounts</c> успевал бы выписать ссылку сброса на чужой
    /// аккаунт. Ролей не осталось — токен не принимается. Запрос в базу на каждый админский
    /// запрос — не в тягость: админов единицы
    /// </summary>
    private static async Task RejectRevokedAdminSession(TokenValidatedContext context)
    {
        await RejectRevokedSession(context);
        if (context.Result != null) return;

        var identity = (ClaimsIdentity)context.Principal!.Identity!;
        var userId = Guid.Parse(identity.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var access = context.HttpContext.RequestServices.GetRequiredService<AdminAccessService>();
        var current = await access.RolesOf(userId, context.HttpContext.RequestAborted);

        foreach (var claim in identity.FindAll(CustomClaimTypes.AdminRole).ToArray())
        {
            if (!int.TryParse(claim.Value, out var role) || !current.Contains((AdminRole)role))
                identity.RemoveClaim(claim);
        }

        if (!identity.HasClaim(x => x.Type == CustomClaimTypes.AdminRole))
            context.Fail("Admin roles revoked");
    }

    private static TokenValidationParameters AdminTokenValidation(AdminOptions admin) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = admin.Issuer,
        ValidAudience = admin.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(admin.SigningKey)),
        ClockSkew = TimeSpan.Zero
    };

    /// <summary>
    /// Ключ продукта — то же правило, что у админского ниже (спец. infrastructure, 9):
    /// пустым не подписать, короткий проще подобрать. Без проверки такой ключ всплыл бы
    /// не на старте, а отказом первого же входа
    /// </summary>
    private static void EnsureProductKeyIsUsable(JwtOptions? product)
    {
        if (string.IsNullOrWhiteSpace(product?.SigningKey))
            throw new InvalidOperationException("JwtOptions:SigningKey is not configured");

        if (Encoding.UTF8.GetByteCount(product.SigningKey) < 32)
            throw new InvalidOperationException("JwtOptions:SigningKey must be at least 32 bytes");
    }

    /// <summary>
    /// Разделение схем держится на двух значениях — аудитории и ключе (спец. admin, 3.3 и 7).
    /// Совпади хоть одно по ошибке конфигурации, и граница истончается до одной строки
    /// проверки; обе совпали — токен продукта открывает админку молча. Такое не стартует.
    ///
    /// Пустой или короткий ключ тоже: подписать пустым нельзя, короткий проще подобрать,
    /// а узнать об этом лучше на старте, чем первым входом в админку
    /// </summary>
    private static void EnsureAdminSchemeIsSeparate(JwtOptions? product, AdminOptions admin)
    {
        if (string.IsNullOrWhiteSpace(admin.SigningKey))
            throw new InvalidOperationException("AdminOptions:SigningKey is not configured");

        if (Encoding.UTF8.GetByteCount(admin.SigningKey) < 32)
            throw new InvalidOperationException("AdminOptions:SigningKey must be at least 32 bytes");

        if (admin.SigningKey == product?.SigningKey)
            throw new InvalidOperationException("AdminOptions:SigningKey must differ from JwtOptions:SigningKey");

        if (admin.Audience == product?.Audience)
            throw new InvalidOperationException("AdminOptions:Audience must differ from JwtOptions:Audience");
    }

    private static IServiceCollection AddControllersWithSwagger(this IServiceCollection services)
    {
        services.AddControllers(options =>
        {
            // Встроенные атрибуты `DataAnnotations` отвечают кодом, а не английской
            // фразой из ресурсов .NET — см. `ValidationCodeMessages`. Добавляется
            // последним намеренно: провайдер `DataAnnotations` к этому моменту уже
            // подставил `[Required]` не-nullable ссылочным свойствам, и под правило
            // попадает в том числе он
            options.ModelMetadataDetailsProviders.Add(new ValidationCodeMessages());

            SetBindingMessagesToCodes(options.ModelBindingMessageProvider);
        });
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            // Из этого документа генерируются типы клиента (`npm run api:types`, спец. infrastructure, 5):
            // nullable-аннотации C# — в `nullable` схемы (у ссылки на схему — через `allOf`),
            // а не «всё ссылочное может быть null»; ненулевое ссылочное — в `required`,
            // как его и требует привязка модели MVC
            options.SupportNonNullableReferenceTypes();
            options.NonNullableReferenceTypesAsRequired();
            options.UseAllOfToExtendReferenceSchemas();
            options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = JwtBearerDefaults.AuthenticationScheme,
                BearerFormat = "JWT",
                Description = "Вставьте JWT токен (без префикса Bearer)"
            });
            options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, doc)] = []
            });
        });

        return services;
    }

    /// <summary>
    /// Вторая половина отказов, которые .NET формулирует сам: сообщения **связывания**
    /// («The value 'abc' is not valid.»). До атрибутов дело тут не доходит — значение
    /// не легло в тип ещё в биндере, — а в теле 422 английская фраза выглядит так же,
    /// как и от атрибута.
    ///
    /// Каждому — код: <c>ValidationError.Parse</c> признаёт его кодом и текст наружу
    /// не отдаёт. Точность здесь грубее, чем у атрибутов (что именно не так со значением,
    /// знает только биндер), поэтому всё, кроме «поля нет» и «тело пустое», уезжает
    /// общим <see cref="ValidationCodes.Invalid"/>
    /// </summary>
    private static void SetBindingMessagesToCodes(DefaultModelBindingMessageProvider messages)
    {
        messages.SetMissingBindRequiredValueAccessor(_ => ValidationCodes.Required);
        messages.SetMissingKeyOrValueAccessor(() => ValidationCodes.Required);
        messages.SetValueMustNotBeNullAccessor(_ => ValidationCodes.Required);
        messages.SetMissingRequestBodyRequiredValueAccessor(() => ValidationCodes.MalformedJson);

        messages.SetAttemptedValueIsInvalidAccessor((_, _) => ValidationCodes.Invalid);
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => ValidationCodes.Invalid);
        messages.SetUnknownValueIsInvalidAccessor(_ => ValidationCodes.Invalid);
        messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => ValidationCodes.Invalid);
        messages.SetValueIsInvalidAccessor(_ => ValidationCodes.Invalid);
        messages.SetValueMustBeANumberAccessor(_ => ValidationCodes.Invalid);
        messages.SetNonPropertyValueMustBeANumberAccessor(() => ValidationCodes.Invalid);
    }

    private static IServiceCollection ConfigureCors(this IServiceCollection services)
    {
        services.AddCors(c => c.AddPolicy("AllowSpecificOrigin", corsBuilder =>
        {
            corsBuilder
                // Основной сайт и админка на своих дев-серверах; прод ходит в API
                // с того же origin через nginx. Поддомен админки (спец. admin, 6) —
                // на случай запроса мимо её собственного /api/
                .WithOrigins("http://localhost:4200", "http://localhost:4201", "https://adminka.example.com")
                .AllowCredentials()
                .AllowAnyMethod()
                .AllowAnyHeader();
        }));

        return services;
    }

    /// <summary>
    /// Адрес клиента из <c>X-Forwarded-For</c>: API стоит за nginx, и без этого у всех
    /// запросов один адрес — контейнера nginx. Нужен лимиту входа в админку
    /// (спец. admin, 3.3) и логам, которые считают попытки и пишут запросы по адресу.
    ///
    /// Доверяем любому соседу, но берём только последнее звено цепочки
    /// (<c>ForwardLimit = 1</c>): его дописывает сам nginx
    /// (<c>$proxy_add_x_forwarded_for</c>), а всё левее прислал клиент и подделать
    /// может. Держится на том, что порт API наружу не смотрит — только через nginx
    /// (<c>127.0.0.1:5000</c>, спец. infrastructure, 6). Списки известных сетей очищены:
    /// адрес nginx в сети докера <c>nginx-proxy-man</c> не постоянный, а по умолчанию
    /// доверяют только loopback — и заголовок бы молча отбрасывался.
    ///
    /// Схема — из <c>X-Forwarded-Proto</c>: nginx ходит в API по http, и без этого
    /// адреса, которые API собирает сам, выходили бы на <c>http://</c>
    /// </summary>
    private static IServiceCollection ConfigureForwardedHeaders(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }

    private static IServiceCollection AddDbContext(this IServiceCollection services, IConfiguration config)
    {
        var connString = config.GetConnectionString("AppConnection");
        // Настройки — те же, что у бандла миграций (snake_case, сборка миграций)
        services.AddDbContext<ApplicationContext>(opt => ApplicationContextFactory.Configure(opt, connString));

        return services;
    }

    /// <summary>
    /// Кольцо ключей Data Protection — в базе (спец. infrastructure, 3.1): контейнер API
    /// выкатка пересоздаёт, и кольцо в нём заводилось бы с нуля на каждый деплой. Имя
    /// приложения — явное: по умолчанию оно берётся из пути сборки, и смена каталога
    /// публикации тоже обнулила бы кольцо
    /// </summary>
    private static IServiceCollection AddKeyRing(this IServiceCollection services)
    {
        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<ApplicationContext>();

        return services;
    }

    private static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddTransient<PermissionsValidation>();
        services.AddTransient<AuthResultFactory>();
        services.AddTransient<AuthService>();
        services.AddTransient<UserNameGenerator>();
        services.AddTransient<AccountService>();
        // Удаление и слияние (спец. accounts, 7 и 10): ключ слияния — в IMemoryCache
        services.AddTransient<AccountCards>();
        services.AddTransient<AccountDeletion>();
        services.AddTransient<AccountMergeService>();
        services.AddTransient<AccountTokens>();
        services.AddTransient<EmailConfirmation>();
        services.AddTransient<PasswordReset>();
        // Счётчики попыток живут в памяти процесса — один на всё приложение (спец. accounts, 8.4;
        // API один, tech-debt/single-instance.md)
        services.AddSingleton<ResetIpLimiter>();
        services.AddTransient<ProfileService>();
        services.AddTransient<WorkspaceService>();
        services.AddTransient<WorkspaceInvitationService>();
        services.AddTransient<UsersService>();
        // Вход в админку и роли (спец. admin, 3.1 и 3.3); разделы «Аккаунты» и «Админы»
        // с приглашениями (3.4, раздел 12)
        services.AddTransient<AdminAccessService>();
        services.AddTransient<AdminAccountService>();
        services.AddTransient<AdminMemberService>();
        services.AddTransient<AdminInvitationService>();
        // «Расход» (спец. admin, 14)
        services.AddTransient<AdminBillingService>();
        // «Модели» и «Диалоги» (спец. admin, 15 и 16): справочник, сверка, разбор,
        // автооценка, журнал доступа и запуски
        services.AddTransient<AdminModelService>();
        services.AddTransient<AdminJobService>();
        services.AddTransient<ModelCheckService>();
        services.AddTransient<AdminDialogService>();
        services.AddTransient<AdminAccessLogService>();
        services.AddTransient<DialogEvaluationService>();
        services.AddTransient<TelegramService>();
        services.AddTransient<IVkService, VkService>();

        // Суточная чистка приглашений — в пространства и в админку, — заброшенных пространств
        // и трасс ассистента (спец. authorization, 6.2 и 8; admin, 3.4; assistant, 13.1)
        services.AddHostedService<DailyCleanupWorker>();

        return services;
    }

    /// <summary>
    /// Векторный индекс (спец. search, 3): эмбеддер на процессоре в соседнем контейнере
    /// и коллекция под его модель. Эмбеддер не настроен — индекса просто нет, поиск идёт
    /// только по словам. Сущности приходят источниками — <see cref="AddSearchSource{TSource}"/>
    /// </summary>
    private static IServiceCollection AddSearch(this IServiceCollection services, IConfiguration config)
    {
        var endpoint = config.GetSection("SearchOptions")["EmbeddingsEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, NullEmbedder>();
        }
        else
        {
            services.AddHttpClient<IEmbeddingGenerator<string, Embedding<float>>, SearchEmbedder>(client =>
            {
                client.BaseAddress = new Uri(endpoint.EndsWith('/') ? endpoint : endpoint + "/");
                // Индексация шлёт все чанки сущности одним запросом, а эмбеддер считает в один
                // поток (IaC/docker-compose.yml) — запас на большую страницу вместо 100 секунд
                // по умолчанию
                client.Timeout = TimeSpan.FromSeconds(60);
            });
        }

        services.AddSingleton<QdrantService>();
        services.AddSingleton<IQdrantMigration, EmbeddingsCollectionMigration>();
        services.AddHostedService<QdrantInitializer>();

        // Справка (спец. help, 6): индекс в памяти, а не в Qdrant — она общая для всех.
        // Эмбеддер тот же; опрос выпуска — первый проход сразу на старте
        services.AddSingleton<HelpSearch>();
        services.AddHostedService<HelpIndexWorker>();

        return services;
    }

    /// <summary>
    /// Ассистент (спец. assistant, 15.2): ход — общей сборкой
    /// <see cref="AssistantRuntime.AddAssistantRuntime"/> (её же берут мир сверки модели
    /// и тесты), поверх — то, что есть только у приложения: тред, голос, живые прогоны,
    /// стартовый справочник, учёт у поставщика. Разделы проекта —
    /// <see cref="AssistantRuntime.AddAssistantSection{TSection}"/> (7.1)
    /// </summary>
    private static IServiceCollection AddAssistant(this IServiceCollection services)
    {
        services.AddAssistantRuntime();

        // Стартовая строка справочника в пустую таблицу (16.2); дальше справочником
        // владеет экран «Модели» в админке (спец. admin, 15)
        services.AddHostedService<AiModelSeeder>();

        // Живые прогоны — синглтон: ход идёт в воркере, а останавливают его из запроса (раздел 3)
        services.AddSingleton<RunCancellation>();
        services.AddScoped<AssistantThreadService>();
        services.AddTransient<VoiceInputService>();

        // Расшифровка голоса (6.2) — тот же поставщик, что у моделей, своя только модель.
        // Решается на каждом запросе по живым настройкам, а не на старте: модели нет
        // или у поставщика нет адреса — голос честно отвечает отказом, а не пятисоткой (6.3)
        services.AddHttpClient(TranscriptionClient, client =>
        {
            // Минута на пятнадцатисекундную реплику — с запасом даже для процессора
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddTransient<ITranscriptionService>(sp =>
        {
            var assistant = sp.GetRequiredService<IOptions<AssistantOptions>>().Value;
            var settings = assistant.Transcription;
            var provider = assistant.Providers.GetValueOrDefault(settings.Provider);
            if (string.IsNullOrWhiteSpace(settings.Model) || string.IsNullOrWhiteSpace(provider?.Endpoint))
                return new NullTranscriptionService();

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(TranscriptionClient);
            http.BaseAddress = new Uri(provider.Endpoint.EndsWith('/') ? provider.Endpoint : provider.Endpoint + "/");

            return new OpenAiCompatibleTranscriptionService(http, provider.ApiKey, settings.Model,
                sp.GetRequiredService<ILogger<OpenAiCompatibleTranscriptionService>>());
        });

        // Лимиты пространств без баланса (13.5–13.6): снимок счёта у поставщика — своим воркером
        services.AddTransient<ProviderLedger>();
        services.AddTransient<WorkspaceLimits>();
        services.AddTransient<BillingMonitor>();
        services.AddHostedService<BillingMonitorWorker>();

        return services;
    }

    /// <summary>
    /// Источник текста для поиска — точка расширения (спец. search, 9): раздел проекта
    /// с текстом регистрирует здесь свой <see cref="ISearchSource"/> под своим
    /// <c>SearchEntityType</c>, и общий обработчик индексации начинает его понимать
    /// </summary>
    public static IServiceCollection AddSearchSource<TSource>(this IServiceCollection services)
        where TSource : class, ISearchSource
    {
        services.AddTransient<ISearchSource, TSource>();

        return services;
    }

    /// <summary>
    /// Очередь команд (спец. mail, 4): постановка — <c>CommandQueue.Enqueue</c> тем же
    /// сохранением, что и доменное изменение, разбор — воркеры пакета <c>AsuTrack.LocalQueue</c>
    /// (<see cref="CommandQueueRegistration"/>). Письма: только обработчик очереди зовёт
    /// отправителя. Индексация поиска — тем же путём, своим воркером
    /// </summary>
    private static IServiceCollection AddCommandQueue(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<MailMetrics>();
        services.AddTransient<IMailSender, SmtpMailSender>();

        var assistant = config.GetSection("AssistantOptions").Get<AssistantOptions>() ?? new AssistantOptions();

        services
            .AddCommandQueueStorage(config)
            // Общий воркер, один цикл: команды идут по одной
            .AddCommandQueueWorker(h => h
                // Письмо: провайдер упал — повтор через минуту, а не потеря (спец. mail, 4)
                .Add<SendMailCommand, SendMailCommandHandler>(TimeSpan.FromMinutes(1)))
            // Индексация поиска (спец. search, 4–5) — своим воркером в один цикл: полный
            // реиндекс ставит команду на каждую сущность, и в общем воркере письмо
            // подтверждения стояло бы за тысячами чанков. Эмбеддер или Qdrant легли — повтор
            // через минуту; чистка пространства идёт тем же воркером, что и запись точек
            .AddCommandQueueWorker(h => h
                .Add<SearchIndexCommand, SearchIndexCommandHandler>()
                .Add<SearchReindexCommand, SearchIndexCommandHandler>()
                .Add<SearchPurgeWorkspaceCommand, SearchIndexCommandHandler>())
            // Ассистент (спец. assistant, раздел 3) — своим воркером: ход идёт минуты,
            // и ни письма за ним, ни он за полным реиндексом поиска не стоят.
            // «Сколько ходов одновременно» — число циклов; выборка по одной строке:
            // взятая пачка ждала бы свободного цикла. Аренда выборки — заведомо больше
            // одного шага (обращения к модели), дальше её продлевает обработчик хода
            .AddCommandQueueWorker(h => h
                    // Попыток на ход — две, повтор через десять секунд: бесконечного повтора
                    // у хода нет, вторую попытку считает сам обработчик (раздел 3)
                    .Add<AssistantRunCommand, AssistantRunCommandHandler>(TimeSpan.FromSeconds(10))
                    .Add<AssistantCompactionCommand, AssistantCompactionCommandHandler>(),
                workers: Math.Max(assistant.Workers, 1),
                prefetchCount: 1,
                invisibilityTimeout: AssistantRunCommandHandler.Lease)
            // Сверка моделей и автооценка из админки (спец. admin, 15.3 и 16.3) — своим
            // воркером в один цикл: сверка идёт минутами и не должна ни занимать циклы
            // ходов людей, ни ждать за ними. Аренда — как у хода, дальше её продлевает сверка
            .AddCommandQueueWorker(h => h
                    .Add<ModelCheckCommand, ModelCheckCommandHandler>()
                    // Отказ поставщика оценка считает исходом, а не бросает: повтор очереди —
                    // только для сбоя базы
                    .Add<DialogEvaluationCommand, DialogEvaluationCommandHandler>(),
                prefetchCount: 1,
                invisibilityTimeout: ModelCheckCommandHandler.Lease);

        return services;
    }
}