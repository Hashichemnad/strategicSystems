using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Strategy.Modules.AuditManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "quick_search",
                schema: "audit_mgmt",
                table: "report",
                type: "text",
                nullable: true,
                computedColumnSql: "btrim(regexp_replace(lower(translate(regexp_replace(coalesce(number::text, '') || ' ' || coalesce(title_ar::text, '') || ' ' || coalesce(title_en::text, '') || ' ' || coalesce(auditor_body::text, ''), '[ً-ٰٟـ]', '', 'g'), 'آأإٱىة٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', 'اااايه01234567890123456789')), '\\s+', ' ', 'g'))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "quick_search",
                schema: "audit_mgmt",
                table: "finding",
                type: "text",
                nullable: true,
                computedColumnSql: "btrim(regexp_replace(lower(translate(regexp_replace(coalesce(number::text, '') || ' ' || coalesce(title_ar::text, '') || ' ' || coalesce(title_en::text, ''), '[ً-ٰٟـ]', '', 'g'), 'آأإٱىة٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹', 'اااايه01234567890123456789')), '\\s+', ' ', 'g'))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_quick_search",
                schema: "audit_mgmt",
                table: "report",
                column: "quick_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_finding_quick_search",
                schema: "audit_mgmt",
                table: "finding",
                column: "quick_search")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_report_quick_search",
                schema: "audit_mgmt",
                table: "report");

            migrationBuilder.DropIndex(
                name: "ix_finding_quick_search",
                schema: "audit_mgmt",
                table: "finding");

            migrationBuilder.DropColumn(
                name: "quick_search",
                schema: "audit_mgmt",
                table: "report");

            migrationBuilder.DropColumn(
                name: "quick_search",
                schema: "audit_mgmt",
                table: "finding");
        }
    }
}
