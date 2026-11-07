using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Benchmarking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "quick_search",
                schema: "bench",
                table: "study",
                type: "text",
                nullable: true,
                computedColumnSql: "btrim(regexp_replace(lower(translate(regexp_replace(coalesce(number::text, '') || ' ' || coalesce(title_ar::text, '') || ' ' || coalesce(title_en::text, '') || ' ' || coalesce(request_number::text, ''), '[ً-ٰٟـ]', '', 'g'), 'آأإٱىة٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', 'اااايه01234567890123456789')), '\\s+', ' ', 'g'))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_study_quick_search",
                schema: "bench",
                table: "study",
                column: "quick_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_study_quick_search",
                schema: "bench",
                table: "study");

            migrationBuilder.DropColumn(
                name: "quick_search",
                schema: "bench",
                table: "study");
        }
    }
}
