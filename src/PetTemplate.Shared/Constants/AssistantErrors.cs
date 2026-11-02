using PetTemplate.Shared.Errors;

namespace PetTemplate.Shared.Constants;

/// <summary>
/// Отказы ассистента кодом (спец. assistant, 14). Перевод — ленивый словарь
/// <c>assistant</c> (<c>i18n/{lang}/assistant.json</c>), а не <c>core</c>: отказ
/// показывает только экран ассистента, и в начальную сборку продукта ему незачем
/// </summary>
public static class AssistantErrors
{
    /// <summary>
    /// Все модели разомкнуты размыкателем или выключены (16.2): ходить некуда. Проверяется
    /// раньше лимита — просить подождать до завтра, когда ассистент всё равно не ответит,
    /// хуже, чем сказать как есть
    /// </summary>
    public static readonly ErrorMessage ModelUnavailable = new(
        "assistant.error.modelUnavailable",
        "Ассистент сейчас недоступен, попробуйте позже");

    /// <summary>
    /// Пространство съело суточный лимит расхода ассистента (13.6). Лимит общий на всех
    /// участников — об этом и текст: «у меня же ничего не было» иначе звучит как поломка
    /// </summary>
    public static readonly ErrorMessage DailyLimitReached = new(
        "assistant.error.dailyLimitReached",
        "В пространстве «{workspace}» исчерпан дневной лимит ассистента — он общий для всех участников. " +
        "Новые просьбы можно будет отправить завтра");

    /// <summary>Пространство съело месячный лимит (13.6)</summary>
    public static readonly ErrorMessage MonthlyLimitReached = new(
        "assistant.error.monthlyLimitReached",
        "В пространстве «{workspace}» исчерпан месячный лимит ассистента — он общий для всех участников. " +
        "Новые просьбы можно будет отправить в следующем месяце");

    /// <summary>
    /// Движок расшифровки не ответил или ответил не тем (6.3). Отказ кодом, а не пятисоткой:
    /// голос — способ заполнить поле ввода, и человек должен понять, что набрать придётся руками
    /// </summary>
    public static readonly ErrorMessage TranscriptionFailed = new(
        "assistant.error.transcriptionFailed",
        "Не удалось распознать запись");

    /// <summary>
    /// Движок расшифровки не настроен вовсе: голос в этой установке не работает,
    /// и сказать об этом надо прямо (6.3)
    /// </summary>
    public static readonly ErrorMessage TranscriptionUnavailable = new(
        "assistant.error.transcriptionUnavailable",
        "Распознавание речи не настроено");

    /// <summary>Запись больше <c>Transcription.MaxAudioBytes</c> (6.1)</summary>
    public static readonly ErrorMessage AudioTooLarge = new(
        "assistant.error.audioTooLarge",
        "Запись слишком большая");

    /// <summary>
    /// Запись длиннее <c>Transcription.MaxSeconds</c> (6.3): дальше это уже не реплика,
    /// а диктовка, и расшифровка съела бы лимит пространства (13.6)
    /// </summary>
    public static readonly ErrorMessage AudioTooLong = new(
        "assistant.error.audioTooLong",
        "Запись слишком длинная — не больше минуты");
}
