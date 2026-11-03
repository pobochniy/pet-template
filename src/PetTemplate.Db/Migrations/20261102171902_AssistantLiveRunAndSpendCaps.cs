using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetTemplate.Db.Migrations
{
    /// <inheritdoc />
    public partial class AssistantLiveRunAndSpendCaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_assistant_runs_running",
                table: "assistant_runs");

            migrationBuilder.AddColumn<bool>(
                name: "cleared",
                table: "assistant_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_service_spend_user_id_created_utc",
                table: "service_spend",
                columns: new[] { "user_id", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_run_spend_user_id_created_utc",
                table: "run_spend",
                columns: new[] { "user_id", "created_utc" });

            // «Один живой ход на тред» теперь — один незаконченный (спец. assistant, раздел 3).
            // Под старым индексом рядом с идущим ходом мог висеть остановленный, но не дописанный:
            // такие, кроме самого свежего в треде, закрываются, иначе индекс не построится
            migrationBuilder.Sql(@"
                UPDATE assistant_runs SET finished_at_utc = now()
                WHERE id IN (
                    SELECT id FROM (
                        SELECT id, row_number() OVER (
                            PARTITION BY user_id, workspace_id ORDER BY started_at_utc DESC, id DESC) AS rn
                        FROM assistant_runs
                        WHERE finished_at_utc IS NULL) live
                    WHERE live.rn > 1)");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_runs_running",
                table: "assistant_runs",
                columns: new[] { "user_id", "workspace_id" },
                unique: true,
                filter: "finished_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_service_spend_user_id_created_utc",
                table: "service_spend");

            migrationBuilder.DropIndex(
                name: "ix_run_spend_user_id_created_utc",
                table: "run_spend");

            migrationBuilder.DropIndex(
                name: "ix_assistant_runs_running",
                table: "assistant_runs");

            migrationBuilder.DropColumn(
                name: "cleared",
                table: "assistant_runs");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_runs_running",
                table: "assistant_runs",
                columns: new[] { "user_id", "workspace_id" },
                unique: true,
                filter: "status = 0");
        }
    }
}
