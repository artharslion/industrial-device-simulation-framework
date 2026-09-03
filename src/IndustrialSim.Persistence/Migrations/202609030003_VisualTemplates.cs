using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IndustrialSim.Persistence.Migrations;

[DbContext(typeof(IndustrialSimDbContext))]
[Migration("202609030003_VisualTemplates")]
public sealed class VisualTemplates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DeviceTemplates",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<string>(type: "TEXT", nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                DeviceType = table.Column<string>(type: "TEXT", nullable: false),
                TagsJson = table.Column<string>(type: "TEXT", nullable: false),
                DocumentJson = table.Column<string>(type: "TEXT", nullable: false),
                CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_DeviceTemplates", item => new { item.Id, item.Version }));
        migrationBuilder.CreateIndex(
            name: "IX_DeviceTemplates_DisplayName_DeviceType",
            table: "DeviceTemplates",
            columns: ["DisplayName", "DeviceType"]);
        migrationBuilder.CreateTable(
            name: "MappingProfiles",
            columns: table => new
            {
                TemplateId = table.Column<string>(type: "TEXT", nullable: false),
                TemplateVersion = table.Column<string>(type: "TEXT", nullable: false),
                Protocol = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                DocumentJson = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MappingProfiles", item => new { item.TemplateId, item.TemplateVersion, item.Protocol, item.Name });
                table.ForeignKey(
                    name: "FK_MappingProfiles_DeviceTemplates_TemplateId_TemplateVersion",
                    columns: item => new { item.TemplateId, item.TemplateVersion },
                    principalTable: "DeviceTemplates",
                    principalColumns: ["Id", "Version"],
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MappingProfiles");
        migrationBuilder.DropTable(name: "DeviceTemplates");
    }
}
