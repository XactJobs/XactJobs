using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XactJobs.TestModel.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
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
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    leased_until = table.Column<DateTime>(type: "datetime2", nullable: true),
                    leaser = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    scheduled_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    type_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    method_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    method_args = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    queue = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    error_count = table.Column<int>(type: "int", nullable: false),
                    error_time = table.Column<DateTime>(type: "datetime2", nullable: true),
                    error_message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    error_stack_trace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    periodic_job_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
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
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    periodic_job_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    cron_expression = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    scheduled_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    type_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    method_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    method_args = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    queue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    error_count = table.Column<int>(type: "int", nullable: false),
                    error_time = table.Column<DateTime>(type: "datetime2", nullable: true),
                    error_message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    error_stack_trace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    periodic_job_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_archive", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_periodic",
                schema: "xact_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    cron_expression = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    type_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    method_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    method_args = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    queue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_periodic", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "User",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_periodic_job_id",
                schema: "xact_jobs",
                table: "job",
                column: "periodic_job_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_job_periodic_name",
                schema: "xact_jobs",
                table: "job_periodic",
                column: "name",
                unique: true);
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

            migrationBuilder.DropTable(
                name: "job_periodic",
                schema: "xact_jobs");

            migrationBuilder.DropTable(
                name: "User");
        }
    }
}
