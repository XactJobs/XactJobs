using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XactJobs.TestModel.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaserIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_job_queue_leaser",
                table: "xact_jobs__job",
                columns: new[] { "queue", "leaser", "leased_until" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_queue_leaser",
                table: "xact_jobs__job");
        }
    }
}
