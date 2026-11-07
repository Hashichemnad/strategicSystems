using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Kpi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "quick_search",
                schema: "kpi",
                table: "kpi_card",
                type: "text",
                nullable: true,
                computedColumnSql: "btrim(regexp_replace(lower(translate(regexp_replace(coalesce(code::text, '') || ' ' || coalesce(name_ar::text, '') || ' ' || coalesce(name_en::text, '') || ' ' || coalesce(process_name::text, ''), '[ً-ٰٟـ]', '', 'g'), 'آأإٱىة٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', 'اااايه01234567890123456789')), '\\s+', ' ', 'g'))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_kpi_card_quick_search",
                schema: "kpi",
                table: "kpi_card",
                column: "quick_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_kpi_card_quick_search",
                schema: "kpi",
                table: "kpi_card");

            migrationBuilder.DropColumn(
                name: "quick_search",
                schema: "kpi",
                table: "kpi_card");
        }
    }
}
