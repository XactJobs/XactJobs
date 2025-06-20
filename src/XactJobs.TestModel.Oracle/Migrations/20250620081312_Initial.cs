using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XactJobs.TestModel.Oracle.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "XACT_JOBS");

            migrationBuilder.CreateTable(
                name: "job",
                schema: "XACT_JOBS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    leased_until = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    leaser = table.Column<Guid>(type: "RAW(16)", nullable: true),
                    status = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    scheduled_at = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    type_name = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    method_name = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    method_args = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    queue = table.Column<string>(type: "NVARCHAR2(450)", nullable: false),
                    error_count = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    error_time = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    error_message = table.Column<string>(type: "NVARCHAR2(2000)", nullable: true),
                    error_stack_trace = table.Column<string>(type: "NVARCHAR2(2000)", nullable: true),
                    periodic_job_id = table.Column<Guid>(type: "RAW(16)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_archive",
                schema: "XACT_JOBS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    completed_at = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    periodic_job_name = table.Column<string>(type: "NVARCHAR2(2000)", nullable: true),
                    cron_expression = table.Column<string>(type: "NVARCHAR2(2000)", nullable: true),
                    status = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    scheduled_at = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    type_name = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    method_name = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    method_args = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    queue = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    error_count = table.Column<int>(type: "NUMBER(10)", nullable: false),
                    error_time = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    error_message = table.Column<string>(type: "NVARCHAR2(2000)", nullable: true),
                    error_stack_trace = table.Column<string>(type: "NVARCHAR2(2000)", nullable: true),
                    periodic_job_id = table.Column<Guid>(type: "RAW(16)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_archive", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_periodic",
                schema: "XACT_JOBS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "RAW(16)", nullable: false),
                    name = table.Column<string>(type: "NVARCHAR2(450)", nullable: false),
                    created_at = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    cron_expression = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    type_name = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    method_name = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    method_args = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    queue = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    is_active = table.Column<int>(type: "NUMBER(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_periodic", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "User",
                columns: table => new
                {
                    Id = table.Column<int>(type: "NUMBER(10)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    FirstName = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    LastName = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false),
                    Email = table.Column<string>(type: "NVARCHAR2(2000)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_periodic_job_id",
                schema: "XACT_JOBS",
                table: "job",
                column: "periodic_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_queue_scheduled_at",
                schema: "XACT_JOBS",
                table: "job",
                columns: new[] { "queue", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_job_archive_completed_at",
                schema: "XACT_JOBS",
                table: "job_archive",
                column: "completed_at");

            migrationBuilder.CreateIndex(
                name: "ix_job_periodic_name",
                schema: "XACT_JOBS",
                table: "job_periodic",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job",
                schema: "XACT_JOBS");

            migrationBuilder.DropTable(
                name: "job_archive",
                schema: "XACT_JOBS");

            migrationBuilder.DropTable(
                name: "job_periodic",
                schema: "XACT_JOBS");

            migrationBuilder.DropTable(
                name: "User");
        }
    }
}
