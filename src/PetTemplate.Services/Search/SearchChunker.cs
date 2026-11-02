using System.Text;

namespace PetTemplate.Services.Search;

/// <summary>
/// Нарезка текста сущности на чанки (спец. search, 3).
///
/// Индексируются чанки, а не сущности целиком: у эмбеддеров окно 512–8000 токенов,
/// качество на длинном тексте падает у всех, а <b>всё за окном обрезается молча</b> —
/// слово в третьем абзаце длинного описания не нашёлся бы никогда, и это
/// не выглядело бы ошибкой. Короткой записи одного чанка обычно хватает, но правило
/// у всех одно.
///
/// Режем по заголовкам и абзацам, склеиваем до нужного размера, перекрываем последним
/// предложением предыдущего чанка: мысль, разорванная границей, иначе теряется
/// в обеих половинах.
/// </summary>
public static class SearchChunker
{
    /// <summary>
    /// Грубая оценка токенов: символов на токен. Точный токенизатор у каждой модели
    /// свой, а промах в полтора раза здесь ничего не решает — важно не превысить окно
    /// </summary>
    private const int CharsPerToken = 4;

    public static List<(int Index, string Text)> Split(string markdown, int chunkTokens)
    {
        var chunks = new List<(int, string)>();
        if (string.IsNullOrWhiteSpace(markdown)) return chunks;

        var limit = Math.Max(chunkTokens, 1) * CharsPerToken;
        var current = new StringBuilder();

        foreach (var block in Blocks(markdown))
        {
            // Заголовок начинает новый чанк: под ним лежит своя тема, и склеивать
            // её с хвостом предыдущей — значит смешивать два ответа в одном
            var isHeading = block.StartsWith('#');

            if (current.Length > 0 && (isHeading || current.Length + block.Length > limit))
            {
                var text = current.ToString().Trim();
                chunks.Add((chunks.Count, text));
                current.Clear();
                current.Append(Overlap(text));
            }

            if (current.Length > 0) current.AppendLine().AppendLine();
            current.Append(block);

            // Один абзац длиннее чанка целиком — режем по границе символов: потерять
            // хвост молча хуже, чем разорвать фразу
            while (current.Length > limit)
            {
                chunks.Add((chunks.Count, current.ToString(0, limit).Trim()));
                var rest = current.ToString(limit, current.Length - limit);
                current.Clear();
                current.Append(rest);
            }
        }

        if (current.ToString().Trim().Length > 0) chunks.Add((chunks.Count, current.ToString().Trim()));

        return chunks;
    }

    /// <summary>Абзацы и заголовки — то, по чему текст режется в первую очередь</summary>
    private static IEnumerable<string> Blocks(string markdown) => markdown
        .Replace("\r\n", "\n")
        .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .SelectMany(x => x.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// Перекрытие — последнее предложение предыдущего чанка
    /// </summary>
    private static string Overlap(string text)
    {
        var end = text.LastIndexOfAny(['.', '!', '?', '\n']);
        if (end <= 0 || end == text.Length - 1)
        {
            var lastDot = text.TrimEnd('.', '!', '?').LastIndexOfAny(['.', '!', '?']);
            if (lastDot <= 0) return string.Empty;

            return text[(lastDot + 1)..].Trim();
        }

        return text[(end + 1)..].Trim();
    }
}
