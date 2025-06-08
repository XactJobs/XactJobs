using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XactJobs.TestModel.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class TestXactJob : Migration
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
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    scheduled_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    leased_until = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    leaser = table.Column<Guid>(type: "uuid", nullable: true),
                    type_name = table.Column<string>(type: "text", nullable: false),
                    method_name = table.Column<string>(type: "text", nullable: false),
                    method_args = table.Column<string>(type: "text", nullable: false),
                    queue = table.Column<string>(type: "text", nullable: false),
                    error_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    error_stack_trace = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_queue_status_scheduled_at",
                schema: "xact_jobs",
                table: "job",
                columns: new[] { "queue", "status", "scheduled_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job",
                schema: "xact_jobs");
        }
    }
}
