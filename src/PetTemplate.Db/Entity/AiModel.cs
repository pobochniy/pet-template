using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetTemplate.Db.Entity;

/// <summary>
/// Строка справочника моделей (спец. assistant, 16.2): какой моделью ходит ассистент
/// и в каком порядке. Цены — для учёта расхода (13.5), экран — «Модели» в админке
/// (спец. admin, 15).
///
/// Секретов здесь нет: адрес и ключ живут в окружении, в секции поставщика
/// <c>AssistantOptions.Providers</c>, а строка связывается с ней кодом
/// <see cref="Provider"/>.
///
/// Модель, на которую ссылаются прогоны, не удаляется, а выключается
/// (<see cref="IsEnabled"/>).
/// </summary>
public class AiModel
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Код поставщика — ключ секции в <c>AssistantOptions.Providers</c>, откуда
    /// берутся адрес, ключ и провайдер-специфичные поля
    /// </summary>
    [MaxLength(64)]
    public required string Provider { get; set; }

    /// <summary>
    /// Идентификатор модели у поставщика, уезжает в запрос. Свободная строка:
    /// у некоторых поставщиков сюда едет URI вида <c>gpt://&lt;folder_id&gt;/…</c> (16.4)
    /// </summary>
    [MaxLength(256)]
    public required string Key { get; set; }

    [MaxLength(128)]
    public required string DisplayName { get; set; }

    /// <summary>
    /// Порядок обращения: первая включённая по возрастанию — основная, дальше
    /// запасные (16.2). Уникален: два первых номера — это недетерминированный выбор
    /// модели, а не «почти правильно»
    /// </summary>
    public int Priority { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>
    /// Своя видеокарта (спец. admin, 16.3): модель стоит у нас, и судье на ней можно
    /// отдать любой диалог — переписка не уходит третьей стороне. Ставит админ: у
    /// поставщика это не спросить
    /// </summary>
    public bool IsLocal { get; set; }

    /// <summary>
    /// Цена за 1000 входящих токенов — по прайсу поставщика, в валюте учёта
    /// (одна на всё приложение, <c>SpendOptions.Currency</c>). Ноль — модель бесплатная
    /// для учёта: токены считаются, деньги — нет (спец. assistant, 13.5)
    /// </summary>
    [Precision(18, 6)]
    public decimal CostIn { get; set; }

    /// <summary>За 1000 входящих, прочитанных поставщиком из кеша</summary>
    [Precision(18, 6)]
    public decimal CostCached { get; set; }

    /// <summary>За 1000 исходящих токенов</summary>
    [Precision(18, 6)]
    public decimal CostOut { get; set; }

    /// <summary>Подряд идущие отказы — для размыкателя (16.2)</summary>
    public int FailureCount { get; set; }

    /// <summary>До какого момента модель разомкнута (16.2)</summary>
    public DateTime? OpenUntilUtc { get; set; }

    /// <summary>Текущая пауза размыкателя; удваивается при повторной неудаче (16.2)</summary>
    public int OpenForSeconds { get; set; }

    public DateTime UpdatedUtc { get; set; }

    /// <summary>
    /// Кто правил строку последним. <c>null</c> — стартовая строка, её завела система
    /// (<c>AssistantOptions.SeedModels</c>), а не человек, или правивший админ удалён
    /// (спец. accounts, раздел 10)
    /// </summary>
    public Guid? UpdatedByUserId { get; set; }

    /// <summary>
    /// Токен конкуренции на всю строку — системная колонка <c>xmin</c> (спец. admin, 15.1):
    /// правка двух админов разом не перезаписывает одна другую. Размыкатель пишет мимо
    /// токена атомарным <c>UPDATE</c>, но <c>xmin</c> двигает и он — правка разомкнувшейся
    /// за это время модели получает «откройте заново»
    /// </summary>
    public uint Version { get; set; }
}

public class AiModelConfiguration : IEntityTypeConfiguration<AiModel>
{
    public void Configure(EntityTypeBuilder<AiModel> builder)
    {
        // Журнал админки переживает админа — ссылка пустеет (спец. accounts, раздел 10)
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Перестановка порядка идёт одной транзакцией, и уникальность
        // проверяет база, а не экран (16.2)
        builder.HasIndex(x => x.Priority).IsUnique();

        builder.Property(x => x.Version).IsRowVersion();

        // Сида в модели нет: миграций у шаблона нет, а справочник — данные, которыми
        // владеет админка. Стартовую строку заводит AiModelSeeder из конфигурации
    }
}
