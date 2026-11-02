using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace PetTemplate.Db.Extensions;

/// <summary>
/// Полнотекстовая половина гибридного поиска (спец. search, 7) — PostgreSQL <c>tsvector</c>
/// с GIN-индексом вместо MySQL <c>FULLTEXT</c> из Lad и ASU (спец. infrastructure, 3.1).
///
/// Колонка вычисляемая (<c>GENERATED ALWAYS AS (to_tsvector(...)) STORED</c>): её пишет сама
/// база при каждой вставке и правке, и забыть обновить её коду нечем. Индекс один, без
/// второго хранилища.
///
/// В шаблоне сущностей с текстом нет — хелпер ждёт раздел проекта (рецепт — спец. search, 9;
/// проверка — тестовая сущность <c>Features/Search/TestNotes</c> в тестовом проекте)
/// </summary>
public static class FullTextSearch
{
    /// <summary>
    /// Конфигурация словарей. <c>russian</c> сводит русские слова к основе («крана» находит
    /// «кран»), а латиницу (<c>asciiword</c>) — английским стеммером: интерфейс шаблона
    /// русский и английский, и одна колонка понимает оба. Та же строка должна стоять
    /// и в колонке, и в запросе — иначе основы не совпадут
    /// </summary>
    public const string Config = "russian";

    /// <summary>
    /// Вычисляемая колонка <c>tsvector</c> по текстовым полям сущности и GIN-индекс по ней.
    /// Зовётся из <c>OnModelCreating</c> (или <c>IEntityTypeConfiguration</c>) раздела;
    /// дальше — миграция, как у любой колонки:
    /// <code>
    /// builder.Entity&lt;Note&gt;().HasFullTextSearch(x =&gt; x.SearchVector, x =&gt; new { x.Title, x.Body });
    /// </code>
    /// </summary>
    /// <param name="vector">Свойство <c>NpgsqlTsVector</c> — сама колонка; руками в неё не пишут</param>
    /// <param name="columns">Текстовые поля в порядке важности; <c>null</c> база пропускает сама</param>
    public static EntityTypeBuilder<TEntity> HasFullTextSearch<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, NpgsqlTsVector>> vector,
        Expression<Func<TEntity, object>> columns)
        where TEntity : class
    {
        builder.HasGeneratedTsVectorColumn(vector, Config, columns);
        // По имени: HasIndex ждёт выражение с object, а у колонки тип свой
        builder.HasIndex(((MemberExpression)vector.Body).Member.Name).HasMethod("GIN");

        return builder;
    }

    /// <summary>
    /// Запрос «любое из слов» для <c>websearch_to_tsquery</c>: как natural language mode у
    /// MySQL <c>MATCH … AGAINST</c> в Lad и ASU, а не «все слова сразу», как по умолчанию
    /// у PostgreSQL. Полнотекстовая половина — про полноту: порядок выдачи решает
    /// <c>ts_rank</c>, а запись с одним словом из трёх в слиянии всё равно уступит найденной
    /// и вектором. <c>websearch_to_tsquery</c> не падает на кавычках, минусах и прочем
    /// вводе человека — синтаксической ошибки у запроса быть не может.
    /// Строка готовится до запроса, а функция и конфигурация — внутри него: EF-функции
    /// переводятся в SQL только в дереве выражения
    /// <code>
    /// var words = FullTextSearch.AnyWord(query);
    /// notes.Where(x =&gt; x.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, words)))
    ///      .OrderByDescending(x =&gt; x.SearchVector.Rank(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, words)))
    /// </code>
    /// </summary>
    public static string AnyWord(string query) =>
        string.Join(" or ", query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
