using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XactJobs.TestModel.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicJobIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_job_periodic_job_id",
                schema: "xact_jobs",
                table: "job",
                column: "periodic_job_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_periodic_job_id",
                schema: "xact_jobs",
                table: "job");
        }
    }
}
