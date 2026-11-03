using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using PetTemplate.Db.Extensions;

namespace PetTemplate.Tests.Features.Search.TestNotes;

/// <summary>
/// Свой контекст тестовой сущности, а не <c>ApplicationContext</c>: подмешивать тестовую
/// таблицу в модель приложения значило бы тащить её в шаблонную базу всех тестов
/// и в будущую миграцию. База — та же, что у приложения теста (строка подключения
/// <c>AppConnection</c>): таблица <c>test_notes</c> встаёт рядом со схемой приложения
/// (<see cref="CreateTable"/>), а очередь команд остаётся общей.
///
/// Колонка <c>tsvector</c> и GIN-индекс — тем же хелпером, каким их заведёт раздел
/// проекта (<see cref="FullTextSearch.HasFullTextSearch{TEntity}"/>)
/// </summary>
public class TestNotesContext(DbContextOptions<TestNotesContext> options) : DbContext(options)
{
    public DbSet<TestNote> Notes => Set<TestNote>();

    /// <summary>
    /// Таблица в базу, где схема приложения уже есть: <c>EnsureCreated</c> на непустой
    /// базе не делает ничего, а создание по модели — только таблицы этого контекста
    /// </summary>
    public Task CreateTable(CancellationToken ct = default) =>
        this.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var note = modelBuilder.Entity<TestNote>();
        note.ToTable("test_notes");
        note.HasIndex(x => x.WorkspaceId);
        note.HasFullTextSearch(x => x.SearchVector, x => new { x.Title, x.Body });
    }
}
