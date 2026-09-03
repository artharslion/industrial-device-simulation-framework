using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IndustrialSim.Persistence.Migrations;

[DbContext(typeof(IndustrialSimDbContext))]
[Migration("202609030004_ScenarioEditor")]
public sealed class ScenarioEditor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "EditorJson", table: "Scenarios", type: "TEXT", nullable: false, defaultValue: "{}");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "EditorJson", table: "Scenarios");
}
