using Microsoft.EntityFrameworkCore;

namespace PetTemplate.Db.Entity;

public class ApplicationContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Profile> Profiles { get; set; }
    public DbSet<UserInPermission> UserInPermissions { get; set; }
    public DbSet<UserInAdminRole> UserInAdminRoles { get; set; }
    public DbSet<Workspace> Workspaces { get; set; }
    public DbSet<WorkspaceInvitation> WorkspaceInvitations { get; set; }
    public DbSet<WorkspaceInvitationPermission> WorkspaceInvitationPermissions { get; set; }
    public DbSet<AccountToken> AccountTokens { get; set; }
    public DbSet<AccountMerge> AccountMerges { get; set; }
    public DbSet<AccountAdminAction> AccountAdminActions { get; set; }
    public DbSet<AdminInvitation> AdminInvitations { get; set; }
    public DbSet<LocalQueue> LocalQueue { get; set; }
    public DbSet<AssistantMessage> AssistantMessages { get; set; }
    public DbSet<AssistantPlan> AssistantPlans { get; set; }
    public DbSet<AssistantRun> AssistantRuns { get; set; }
    public DbSet<AiModel> AiModels { get; set; }
    public DbSet<AssistantTraceStep> AssistantTraceSteps { get; set; }
    public DbSet<AssistantFact> AssistantFacts { get; set; }
    public DbSet<RunSpend> RunSpend { get; set; }
    public DbSet<ServiceSpend> ServiceSpend { get; set; }
    public DbSet<ProviderLedgerEntry> ProviderLedger { get; set; }
    public DbSet<WorkspaceAssistantLimit> WorkspaceAssistantLimits { get; set; }
    public DbSet<AdminJob> AdminJobs { get; set; }
    public DbSet<AdminAccessLog> AdminAccessLog { get; set; }
    public DbSet<DialogEvaluation> DialogEvaluations { get; set; }

    // Опции — свои, типизированные: с общими DbContextOptions второй контекст в том же
    // DI (тестовый контекст поиска, Features/Search) подсунул бы ему свои
    public ApplicationContext(DbContextOptions<ApplicationContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Логин и почта сравниваются без учёта регистра (спец. infrastructure, 3.1):
        // это даёт тип citext. Расширение ставит миграция
        builder.HasPostgresExtension("citext");

        builder.ApplyConfiguration(new UserConfiguration());
        builder.ApplyConfiguration(new ProfileConfiguration());
        builder.ApplyConfiguration(new UserInPermissionConfiguration());
        builder.ApplyConfiguration(new UserInAdminRoleConfiguration());
        builder.ApplyConfiguration(new WorkspaceInvitationConfiguration());
        builder.ApplyConfiguration(new WorkspaceInvitationPermissionConfiguration());
        builder.ApplyConfiguration(new AccountTokenConfiguration());
        builder.ApplyConfiguration(new AccountMergeConfiguration());
        builder.ApplyConfiguration(new AccountAdminActionConfiguration());
        builder.ApplyConfiguration(new AdminInvitationConfiguration());
        builder.ApplyConfiguration(new LocalQueueConfiguration());
        builder.ApplyConfiguration(new AssistantMessageConfiguration());
        builder.ApplyConfiguration(new AssistantPlanConfiguration());
        builder.ApplyConfiguration(new AssistantRunConfiguration());
        builder.ApplyConfiguration(new AiModelConfiguration());
        builder.ApplyConfiguration(new AssistantTraceStepConfiguration());
        builder.ApplyConfiguration(new AssistantFactConfiguration());
        builder.ApplyConfiguration(new RunSpendConfiguration());
        builder.ApplyConfiguration(new ServiceSpendConfiguration());
        builder.ApplyConfiguration(new ProviderLedgerEntryConfiguration());
        builder.ApplyConfiguration(new WorkspaceAssistantLimitConfiguration());
        builder.ApplyConfiguration(new AdminJobConfiguration());
        builder.ApplyConfiguration(new AdminAccessLogConfiguration());
        builder.ApplyConfiguration(new DialogEvaluationConfiguration());
    }
}