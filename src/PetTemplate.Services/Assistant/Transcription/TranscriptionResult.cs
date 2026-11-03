namespace PetTemplate.Services.Assistant.Transcription;

/// <summary>
/// Что вернул движок: текст и то, по чему считать расход (13.5). Длительность и токены
/// есть не у всех движков — чего движок не назвал, остаётся пустым, и считает
/// вызывающий по тому, что знает сам
/// </summary>
/// <param name="Seconds">Длительность записи по словам движка (<c>usage.seconds</c> или <c>duration</c>)</param>
/// <param name="TokensIn">Входящие токены — у движков, считающих аудио токенами</param>
/// <param name="TokensOut">Исходящие токены — текст расшифровки</param>
public record TranscriptionResult(string Text, double? Seconds = null, long TokensIn = 0, long TokensOut = 0);
