using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XactJobs.TestModel.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddXactJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "xact_jobs");

            migrationBuilder.CreateTable(
                name: "job",
                schema: "xact_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leased_until = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    leaser = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    scheduled_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    type_name = table.Column<string>(type: "text", nullable: false),
                    method_name = table.Column<string>(type: "text", nullable: false),
                    method_args = table.Column<string>(type: "text", nullable: false),
                    queue = table.Column<string>(type: "text", nullable: false),
                    error_count = table.Column<int>(type: "integer", nullable: false),
                    error_time = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    error_stack_trace = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_archive",
                schema: "xact_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    scheduled_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    type_name = table.Column<string>(type: "text", nullable: false),
                    method_name = table.Column<string>(type: "text", nullable: false),
                    method_args = table.Column<string>(type: "text", nullable: false),
                    queue = table.Column<string>(type: "text", nullable: false),
                    error_count = table.Column<int>(type: "integer", nullable: false),
                    error_time = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    error_stack_trace = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_archive", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_queue_scheduled_at",
                schema: "xact_jobs",
                table: "job",
                columns: new[] { "queue", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_job_archive_completed_at",
                schema: "xact_jobs",
                table: "job_archive",
                column: "completed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job",
                schema: "xact_jobs");

            migrationBuilder.DropTable(
                name: "job_archive",
                schema: "xact_jobs");
        }
    }
}
