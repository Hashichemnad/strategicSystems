using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Requests.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_request_title_search",
                schema: "requests",
                table: "request",
                column: "title_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_request_title_search",
                schema: "requests",
                table: "request");
        }
    }
}
