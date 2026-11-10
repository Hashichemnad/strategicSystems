using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.IpRegistry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_ip_record_search_text",
                schema: "ip",
                table: "ip_record",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ip_record_search_text",
                schema: "ip",
                table: "ip_record");
        }
    }
}
