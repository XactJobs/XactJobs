using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XactJobs.TestModel.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class RemovePeriodicFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_job_periodic_job_id",
                schema: "xact_jobs",
                table: "job");

            migrationBuilder.DropIndex(
                name: "ix_job_periodic_job_id",
                schema: "xact_jobs",
                table: "job");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_job_periodic_job_id",
                schema: "xact_jobs",
                table: "job",
                column: "periodic_job_id");

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
    }
}
