using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Teams.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "quick_search",
                schema: "teams",
                table: "team",
                type: "text",
                nullable: true,
                computedColumnSql: "btrim(regexp_replace(lower(translate(regexp_replace(coalesce(number::text, '') || ' ' || coalesce(name_ar::text, '') || ' ' || coalesce(name_en::text, '') || ' ' || coalesce(subject::text, ''), '[ً-ٰٟـ]', '', 'g'), 'آأإٱىة٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', 'اااايه01234567890123456789')), '\\s+', ' ', 'g'))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "quick_search",
                schema: "teams",
                table: "recommendation",
                type: "text",
                nullable: true,
                computedColumnSql: "btrim(regexp_replace(lower(translate(regexp_replace(coalesce(number::text, '') || ' ' || coalesce(text::text, ''), '[ً-ٰٟـ]', '', 'g'), 'آأإٱىة٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', 'اااايه01234567890123456789')), '\\s+', ' ', 'g'))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "quick_search",
                schema: "teams",
                table: "meeting",
                type: "text",
                nullable: true,
                computedColumnSql: "btrim(regexp_replace(lower(translate(regexp_replace(coalesce(number::text, '') || ' ' || coalesce(title::text, '') || ' ' || coalesce(location::text, ''), '[ً-ٰٟـ]', '', 'g'), 'آأإٱىة٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', 'اااايه01234567890123456789')), '\\s+', ' ', 'g'))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_team_quick_search",
                schema: "teams",
                table: "team",
                column: "quick_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_quick_search",
                schema: "teams",
                table: "recommendation",
                column: "quick_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_quick_search",
                schema: "teams",
                table: "meeting",
                column: "quick_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_team_quick_search",
                schema: "teams",
                table: "team");

            migrationBuilder.DropIndex(
                name: "ix_recommendation_quick_search",
                schema: "teams",
                table: "recommendation");

            migrationBuilder.DropIndex(
                name: "ix_meeting_quick_search",
                schema: "teams",
                table: "meeting");

            migrationBuilder.DropColumn(
                name: "quick_search",
                schema: "teams",
                table: "team");

            migrationBuilder.DropColumn(
                name: "quick_search",
                schema: "teams",
                table: "recommendation");

            migrationBuilder.DropColumn(
                name: "quick_search",
                schema: "teams",
                table: "meeting");
        }
    }
}
