using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PetTemplate.Db.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "local_queue",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    command_type = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    locked_till_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    try_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_local_queue", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vk_id = table.Column<long>(type: "bigint", nullable: true),
                    telegram_id = table.Column<long>(type: "bigint", nullable: true),
                    telegram_linked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    vk_linked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tokens_valid_after_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_login_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workspaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspaces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_admin_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    admin_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_user_name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_admin_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_admin_actions_users_admin_user_id",
                        column: x => x.admin_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "account_merges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kept_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kept_user_name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    removed_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    removed_user_name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_merges", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_merges_users_kept_user_id",
                        column: x => x.kept_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "account_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    mail_sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    issued_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_tokens_users_issued_by_user_id",
                        column: x => x.issued_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_account_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "admin_access_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_access_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_admin_access_log_users_admin_user_id",
                        column: x => x.admin_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "admin_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    roles = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    expires_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_admin_invitations_users_accepted_by_user_id",
                        column: x => x.accepted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_admin_invitations_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_admin_invitations_users_revoked_by_user_id",
                        column: x => x.revoked_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ai_models",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_local = table.Column<bool>(type: "boolean", nullable: false),
                    cost_in = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    cost_cached = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    cost_out = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    open_until_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    open_for_seconds = table.Column<int>(type: "integer", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_models", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_models_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "provider_ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    period_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    period_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_ledger", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_ledger_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_in_admin_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    granted_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_in_admin_roles", x => new { x.user_id, x.role });
                    table.ForeignKey(
                        name: "fk_user_in_admin_roles_users_granted_by_user_id",
                        column: x => x.granted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_user_in_admin_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assistant_facts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_used_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    use_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_facts", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_facts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assistant_facts_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assistant_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    operations = table.Column<string>(type: "jsonb", nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    applied_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_plans", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_plans_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assistant_plans_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "citext", maxLength: 20, nullable: false),
                    user_name_is_generated = table.Column<bool>(type: "boolean", nullable: false),
                    avatar_id = table.Column<int>(type: "integer", nullable: true),
                    email = table.Column<string>(type: "citext", maxLength: 255, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    comment = table.Column<string>(type: "text", maxLength: 3000, nullable: true),
                    is_adult = table.Column<bool>(type: "boolean", nullable: false),
                    has_accepted_terms = table.Column<bool>(type: "boolean", nullable: false),
                    primary_workspace_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_profiles_users_id",
                        column: x => x.id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_profiles_workspaces_primary_workspace_id",
                        column: x => x.primary_workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_in_permissions",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_in_permissions", x => new { x.workspace_id, x.user_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_user_in_permissions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_in_permissions_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspace_assistant_limits",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tokens_per_day = table.Column<long>(type: "bigint", nullable: true),
                    tokens_per_month = table.Column<long>(type: "bigint", nullable: true),
                    cost_per_day = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    cost_per_month = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_assistant_limits", x => x.workspace_id);
                    table.ForeignKey(
                        name: "fk_workspace_assistant_limits_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_workspace_assistant_limits_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "admin_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    planned = table.Column<int>(type: "integer", nullable: false),
                    done = table.Column<int>(type: "integer", nullable: false),
                    skipped = table.Column<int>(type: "integer", nullable: false),
                    failed = table.Column<int>(type: "integer", nullable: false),
                    tokens_in = table.Column<long>(type: "bigint", nullable: false),
                    tokens_cached = table.Column<long>(type: "bigint", nullable: false),
                    tokens_out = table.Column<long>(type: "bigint", nullable: false),
                    cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    report = table.Column<string>(type: "jsonb", nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_admin_jobs_ai_models_model_id",
                        column: x => x.model_id,
                        principalTable: "ai_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_admin_jobs_users_started_by_user_id",
                        column: x => x.started_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "assistant_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    step = table.Column<int>(type: "integer", nullable: false),
                    escalated = table.Column<bool>(type: "boolean", nullable: false),
                    stop_reason = table.Column<int>(type: "integer", nullable: true),
                    tokens_in = table.Column<long>(type: "bigint", nullable: false),
                    tokens_cached = table.Column<long>(type: "bigint", nullable: false),
                    tokens_out = table.Column<long>(type: "bigint", nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    current_activity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    ai_model_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_runs_ai_models_ai_model_id",
                        column: x => x.ai_model_id,
                        principalTable: "ai_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assistant_runs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assistant_runs_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dialog_evaluations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: true),
                    satisfaction = table.Column<int>(type: "integer", nullable: true),
                    clarifications = table.Column<int>(type: "integer", nullable: true),
                    replans = table.Column<int>(type: "integer", nullable: true),
                    agent_mistake = table.Column<bool>(type: "boolean", nullable: true),
                    stop_reason = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dialog_evaluations", x => x.id);
                    table.ForeignKey(
                        name: "fk_dialog_evaluations_ai_models_model_id",
                        column: x => x.model_id,
                        principalTable: "ai_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "run_spend",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tokens_in = table.Column<long>(type: "bigint", nullable: false),
                    tokens_cached = table.Column<long>(type: "bigint", nullable: false),
                    tokens_out = table.Column<long>(type: "bigint", nullable: false),
                    cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_run_spend", x => x.id);
                    table.ForeignKey(
                        name: "fk_run_spend_ai_models_model_id",
                        column: x => x.model_id,
                        principalTable: "ai_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_spend",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    tokens_in = table.Column<long>(type: "bigint", nullable: false),
                    tokens_cached = table.Column<long>(type: "bigint", nullable: false),
                    tokens_out = table.Column<long>(type: "bigint", nullable: false),
                    seconds = table.Column<int>(type: "integer", nullable: false),
                    cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_spend", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_spend_ai_models_model_id",
                        column: x => x.model_id,
                        principalTable: "ai_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspace_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    token = table.Column<Guid>(type: "uuid", nullable: false),
                    invitee_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    accepted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    accepted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_workspace_invitations_profiles_accepted_by",
                        column: x => x.accepted_by,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_workspace_invitations_profiles_created_by",
                        column: x => x.created_by,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_workspace_invitations_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assistant_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    options = table.Column<string>(type: "jsonb", nullable: true),
                    fact_ids = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_messages_assistant_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "assistant_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_assistant_messages_assistant_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "assistant_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_assistant_messages_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assistant_messages_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assistant_trace_steps",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tool_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    input = table.Column<string>(type: "text", nullable: false),
                    output = table.Column<string>(type: "text", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    tokens_in = table.Column<int>(type: "integer", nullable: false),
                    tokens_out = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_trace_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_trace_steps_assistant_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "assistant_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspace_invitation_permissions",
                columns: table => new
                {
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_invitation_permissions", x => new { x.invitation_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_workspace_invitation_permissions_workspace_invitations_invi",
                        column: x => x.invitation_id,
                        principalTable: "workspace_invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_admin_actions_admin_user_id",
                table: "account_admin_actions",
                column: "admin_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_admin_actions_created_utc",
                table: "account_admin_actions",
                column: "created_utc");

            migrationBuilder.CreateIndex(
                name: "ix_account_admin_actions_target_user_id",
                table: "account_admin_actions",
                column: "target_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_merges_kept_user_id",
                table: "account_merges",
                column: "kept_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_merges_removed_user_id",
                table: "account_merges",
                column: "removed_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_tokens_hash",
                table: "account_tokens",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_tokens_issued_by_user_id",
                table: "account_tokens",
                column: "issued_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_tokens_user_id_kind",
                table: "account_tokens",
                columns: new[] { "user_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_access_log_admin_user_id_created_utc",
                table: "admin_access_log",
                columns: new[] { "admin_user_id", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_access_log_created_utc",
                table: "admin_access_log",
                column: "created_utc");

            migrationBuilder.CreateIndex(
                name: "ix_admin_invitations_accepted_by_user_id",
                table: "admin_invitations",
                column: "accepted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_admin_invitations_created_by_user_id",
                table: "admin_invitations",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_admin_invitations_created_utc",
                table: "admin_invitations",
                column: "created_utc");

            migrationBuilder.CreateIndex(
                name: "ix_admin_invitations_revoked_by_user_id",
                table: "admin_invitations",
                column: "revoked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_admin_invitations_token_hash",
                table: "admin_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_admin_jobs_kind_created_utc",
                table: "admin_jobs",
                columns: new[] { "kind", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_jobs_model_id",
                table: "admin_jobs",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_admin_jobs_running",
                table: "admin_jobs",
                column: "kind",
                unique: true,
                filter: "status = 0");

            migrationBuilder.CreateIndex(
                name: "ix_admin_jobs_started_by_user_id",
                table: "admin_jobs",
                column: "started_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_models_priority",
                table: "ai_models",
                column: "priority",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_models_updated_by_user_id",
                table: "ai_models",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_facts_user_id",
                table: "assistant_facts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_facts_workspace_id_user_id_kind_key",
                table: "assistant_facts",
                columns: new[] { "workspace_id", "user_id", "kind", "key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_assistant_messages_plan_id",
                table: "assistant_messages",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_messages_run_id",
                table: "assistant_messages",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_messages_user_id_workspace_id_id",
                table: "assistant_messages",
                columns: new[] { "user_id", "workspace_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_messages_workspace_id",
                table: "assistant_messages",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_plans_user_id_workspace_id_status",
                table: "assistant_plans",
                columns: new[] { "user_id", "workspace_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_plans_workspace_id",
                table: "assistant_plans",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_runs_ai_model_id",
                table: "assistant_runs",
                column: "ai_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_runs_running",
                table: "assistant_runs",
                columns: new[] { "user_id", "workspace_id" },
                unique: true,
                filter: "status = 0");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_runs_user_id_workspace_id_status",
                table: "assistant_runs",
                columns: new[] { "user_id", "workspace_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_runs_workspace_id",
                table: "assistant_runs",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_trace_steps_created_at_utc",
                table: "assistant_trace_steps",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_trace_steps_run_id_step",
                table: "assistant_trace_steps",
                columns: new[] { "run_id", "step" });

            migrationBuilder.CreateIndex(
                name: "ix_dialog_evaluations_created_utc",
                table: "dialog_evaluations",
                column: "created_utc");

            migrationBuilder.CreateIndex(
                name: "ix_dialog_evaluations_model_id",
                table: "dialog_evaluations",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_dialog_evaluations_run_id",
                table: "dialog_evaluations",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_queue_command_type_created_at_utc",
                table: "local_queue",
                columns: new[] { "command_type", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_profiles_email",
                table: "profiles",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_profiles_primary_workspace_id",
                table: "profiles",
                column: "primary_workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_profiles_user_name",
                table: "profiles",
                column: "user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_ledger_actor_user_id",
                table: "provider_ledger",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_ledger_provider_kind",
                table: "provider_ledger",
                columns: new[] { "provider", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_run_spend_created_utc",
                table: "run_spend",
                column: "created_utc");

            migrationBuilder.CreateIndex(
                name: "ix_run_spend_model_id",
                table: "run_spend",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_run_spend_run_id",
                table: "run_spend",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_run_spend_workspace_id_created_utc",
                table: "run_spend",
                columns: new[] { "workspace_id", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_service_spend_created_utc",
                table: "service_spend",
                column: "created_utc");

            migrationBuilder.CreateIndex(
                name: "ix_service_spend_model_id",
                table: "service_spend",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_spend_workspace_id_created_utc",
                table: "service_spend",
                columns: new[] { "workspace_id", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_user_in_admin_roles_granted_by_user_id",
                table: "user_in_admin_roles",
                column: "granted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_in_permissions_user_id",
                table: "user_in_permissions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_telegram_id",
                table: "users",
                column: "telegram_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_vk_id",
                table: "users",
                column: "vk_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_assistant_limits_updated_by_user_id",
                table: "workspace_assistant_limits",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_invitations_accepted_by",
                table: "workspace_invitations",
                column: "accepted_by");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_invitations_created_at_utc",
                table: "workspace_invitations",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_invitations_created_by",
                table: "workspace_invitations",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_invitations_token",
                table: "workspace_invitations",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_invitations_workspace_id",
                table: "workspace_invitations",
                column: "workspace_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_admin_actions");

            migrationBuilder.DropTable(
                name: "account_merges");

            migrationBuilder.DropTable(
                name: "account_tokens");

            migrationBuilder.DropTable(
                name: "admin_access_log");

            migrationBuilder.DropTable(
                name: "admin_invitations");

            migrationBuilder.DropTable(
                name: "admin_jobs");

            migrationBuilder.DropTable(
                name: "assistant_facts");

            migrationBuilder.DropTable(
                name: "assistant_messages");

            migrationBuilder.DropTable(
                name: "assistant_trace_steps");

            migrationBuilder.DropTable(
                name: "dialog_evaluations");

            migrationBuilder.DropTable(
                name: "local_queue");

            migrationBuilder.DropTable(
                name: "provider_ledger");

            migrationBuilder.DropTable(
                name: "run_spend");

            migrationBuilder.DropTable(
                name: "service_spend");

            migrationBuilder.DropTable(
                name: "user_in_admin_roles");

            migrationBuilder.DropTable(
                name: "user_in_permissions");

            migrationBuilder.DropTable(
                name: "workspace_assistant_limits");

            migrationBuilder.DropTable(
                name: "workspace_invitation_permissions");

            migrationBuilder.DropTable(
                name: "assistant_plans");

            migrationBuilder.DropTable(
                name: "assistant_runs");

            migrationBuilder.DropTable(
                name: "workspace_invitations");

            migrationBuilder.DropTable(
                name: "ai_models");

            migrationBuilder.DropTable(
                name: "profiles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "workspaces");
        }
    }
}
