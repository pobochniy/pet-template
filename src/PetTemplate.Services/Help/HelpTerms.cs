using System.Text;

namespace PetTemplate.Services.Help;

/// <summary>
/// Слова для ключевой половины поиска справки (спец. help, 6.3): нижний регистр,
/// <c>ё</c> → <c>е</c>, русский стеммер Snowball. «Пополнить», «пополнение»,
/// «пополню» сходятся к одной основе — без этого вопрос и ответ, сказанные разными
/// формами одного слова, друг друга не нашли бы
/// </summary>
public static class HelpTerms
{
    /// <summary>
    /// Служебные слова: они есть почти в каждом разделе и ранжированию только мешают.
    /// Список короткий нарочно — BM25 и сам гасит частые слова, а лишнее стоп-слово
    /// теряет смысл («не», «нет» здесь нет: «почему не отвечает» — вопрос про отказ)
    /// </summary>
    private static readonly HashSet<string> Stop =
    [
        "и", "в", "во", "на", "с", "со", "к", "ко", "у", "о", "об", "а", "но", "по", "из", "за", "для",
        "от", "до", "же", "ли", "бы", "это", "то", "как", "что", "или", "мне", "меня", "мой", "моя",
    ];

    public static List<string> Of(string text)
    {
        // Стеммер держит буфер внутри — на каждый текст свой, индекс строится и ищется
        // из разных потоков
        var stemmer = new Snowball.RussianStemmer();
        var terms = new List<string>();
        var word = new StringBuilder();

        foreach (var c in text.Append(' '))
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(c == 'ё' || c == 'Ё' ? 'е' : char.ToLowerInvariant(c));
                continue;
            }

            if (word.Length == 0) continue;

            var value = word.ToString();
            word.Clear();
            if (Stop.Contains(value)) continue;

            terms.Add(IsCyrillic(value) ? stemmer.Stem(value) : value);
        }

        return terms;
    }

    private static bool IsCyrillic(string word) => word.Any(c => c is >= 'а' and <= 'я');
}
