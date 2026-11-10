using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.Portfolio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "quick_search",
                schema: "portfolio",
                table: "user_story",
                type: "text",
                nullable: true,
                computedColumnSql: "btrim(regexp_replace(lower(translate(regexp_replace(coalesce(code::text, '') || ' ' || coalesce(title::text, ''), '[ً-ٰٟـ]', '', 'g'), 'آأإٱىة٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', 'اااايه01234567890123456789')), '\\s+', ' ', 'g'))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "quick_search",
                schema: "portfolio",
                table: "request",
                type: "text",
                nullable: true,
                computedColumnSql: "btrim(regexp_replace(lower(translate(regexp_replace(coalesce(number::text, '') || ' ' || coalesce(name_ar::text, '') || ' ' || coalesce(name_en::text, '') || ' ' || coalesce(partner_name::text, '') || ' ' || coalesce(previous_program_name::text, ''), '[ً-ٰٟـ]', '', 'g'), 'آأإٱىة٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', 'اااايه01234567890123456789')), '\\s+', ' ', 'g'))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "quick_search",
                schema: "portfolio",
                table: "project",
                type: "text",
                nullable: true,
                computedColumnSql: "btrim(regexp_replace(lower(translate(regexp_replace(coalesce(number::text, '') || ' ' || coalesce(name_ar::text, '') || ' ' || coalesce(name_en::text, ''), '[ً-ٰٟـ]', '', 'g'), 'آأإٱىة٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', 'اااايه01234567890123456789')), '\\s+', ' ', 'g'))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "quick_search",
                schema: "portfolio",
                table: "milestone",
                type: "text",
                nullable: true,
                computedColumnSql: "btrim(regexp_replace(lower(translate(regexp_replace(coalesce(code::text, '') || ' ' || coalesce(title_ar::text, '') || ' ' || coalesce(title_en::text, '') || ' ' || coalesce(main_deliverable::text, ''), '[ً-ٰٟـ]', '', 'g'), 'آأإٱىة٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', 'اااايه01234567890123456789')), '\\s+', ' ', 'g'))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_story_quick_search",
                schema: "portfolio",
                table: "user_story",
                column: "quick_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_request_quick_search",
                schema: "portfolio",
                table: "request",
                column: "quick_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_project_quick_search",
                schema: "portfolio",
                table: "project",
                column: "quick_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_milestone_quick_search",
                schema: "portfolio",
                table: "milestone",
                column: "quick_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_story_quick_search",
                schema: "portfolio",
                table: "user_story");

            migrationBuilder.DropIndex(
                name: "ix_request_quick_search",
                schema: "portfolio",
                table: "request");

            migrationBuilder.DropIndex(
                name: "ix_project_quick_search",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropIndex(
                name: "ix_milestone_quick_search",
                schema: "portfolio",
                table: "milestone");

            migrationBuilder.DropColumn(
                name: "quick_search",
                schema: "portfolio",
                table: "user_story");

            migrationBuilder.DropColumn(
                name: "quick_search",
                schema: "portfolio",
                table: "request");

            migrationBuilder.DropColumn(
                name: "quick_search",
                schema: "portfolio",
                table: "project");

            migrationBuilder.DropColumn(
                name: "quick_search",
                schema: "portfolio",
                table: "milestone");
        }
    }
}
