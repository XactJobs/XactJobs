using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XactJobs.TestModel.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cron_expression",
                schema: "xact_jobs",
                table: "job_archive",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "periodic_job_id",
                schema: "xact_jobs",
                table: "job_archive",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "periodic_job_name",
                schema: "xact_jobs",
                table: "job_archive",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "periodic_job_id",
                schema: "xact_jobs",
                table: "job",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "job_periodic",
                schema: "xact_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    cron_expression = table.Column<string>(type: "text", nullable: false),
                    type_name = table.Column<string>(type: "text", nullable: false),
                    method_name = table.Column<string>(type: "text", nullable: false),
                    method_args = table.Column<string>(type: "text", nullable: false),
                    queue = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_periodic", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_periodic_job_id",
                schema: "xact_jobs",
                table: "job",
                column: "periodic_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_periodic_name",
                schema: "xact_jobs",
                table: "job_periodic",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_job_periodic_job_id",
                schema: "xact_jobs",
                table: "job",
                column: "periodic_job_id",
                principalSchema: "xact_jobs",
                principalTable: "job_periodic",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_job_periodic_job_id",
                schema: "xact_jobs",
                table: "job");

            migrationBuilder.DropTable(
                name: "job_periodic",
                schema: "xact_jobs");

            migrationBuilder.DropIndex(
                name: "ix_job_periodic_job_id",
                schema: "xact_jobs",
                table: "job");

            migrationBuilder.DropColumn(
                name: "cron_expression",
                schema: "xact_jobs",
                table: "job_archive");

            migrationBuilder.DropColumn(
                name: "periodic_job_id",
                schema: "xact_jobs",
                table: "job_archive");

            migrationBuilder.DropColumn(
                name: "periodic_job_name",
                schema: "xact_jobs",
                table: "job_archive");

            migrationBuilder.DropColumn(
                name: "periodic_job_id",
                schema: "xact_jobs",
                table: "job");
        }
    }
}
