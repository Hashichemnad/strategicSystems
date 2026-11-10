using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Risk.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "quick_search",
                schema: "risk",
                table: "treatment_action",
                type: "text",
                nullable: true,
                computedColumnSql: "btrim(regexp_replace(lower(translate(regexp_replace(coalesce(number::text, '') || ' ' || coalesce(title_ar::text, '') || ' ' || coalesce(title_en::text, ''), '[ً-ٰٟـ]', '', 'g'), 'آأإٱىة٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', 'اااايه01234567890123456789')), '\\s+', ' ', 'g'))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "quick_search",
                schema: "risk",
                table: "risk",
                type: "text",
                nullable: true,
                computedColumnSql: "btrim(regexp_replace(lower(translate(regexp_replace(coalesce(number::text, '') || ' ' || coalesce(title_ar::text, '') || ' ' || coalesce(title_en::text, '') || ' ' || coalesce(event_text::text, ''), '[ً-ٰٟـ]', '', 'g'), 'آأإٱىة٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', 'اااايه01234567890123456789')), '\\s+', ' ', 'g'))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_treatment_action_quick_search",
                schema: "risk",
                table: "treatment_action",
                column: "quick_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_risk_quick_search",
                schema: "risk",
                table: "risk",
                column: "quick_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_treatment_action_quick_search",
                schema: "risk",
                table: "treatment_action");

            migrationBuilder.DropIndex(
                name: "ix_risk_quick_search",
                schema: "risk",
                table: "risk");

            migrationBuilder.DropColumn(
                name: "quick_search",
                schema: "risk",
                table: "treatment_action");

            migrationBuilder.DropColumn(
                name: "quick_search",
                schema: "risk",
                table: "risk");
        }
    }
}
