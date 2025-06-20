using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XactJobs.TestModel.Oracle.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaserIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_job_queue_leaser",
                schema: "XACT_JOBS",
                table: "job",
                columns: new[] { "queue", "leaser", "leased_until" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_queue_leaser",
                schema: "XACT_JOBS",
                table: "job");
        }
    }
}
