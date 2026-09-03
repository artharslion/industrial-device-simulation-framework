using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IndustrialSim.Persistence.Migrations;

[DbContext(typeof(IndustrialSimDbContext))]
[Migration("202609030001_InitialControlPlane")]
public sealed class InitialControlPlane : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Devices",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                DesiredState = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Devices", item => item.Id));
        migrationBuilder.CreateTable(
            name: "Scenarios",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Yaml = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Scenarios", item => item.Id));
        migrationBuilder.CreateTable(
            name: "Settings",
            columns: table => new
            {
                Key = table.Column<string>(type: "TEXT", nullable: false),
                ValueJson = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Settings", item => item.Key));
        migrationBuilder.CreateTable(
            name: "RuntimeSnapshots",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                DeviceId = table.Column<string>(type: "TEXT", nullable: false),
                SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Seed = table.Column<int>(type: "INTEGER", nullable: false),
                SimulationTimeTicks = table.Column<long>(type: "INTEGER", nullable: false),
                DefinitionFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                StateJson = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_RuntimeSnapshots", item => item.Id));
        migrationBuilder.CreateIndex(
            name: "IX_RuntimeSnapshots_DeviceId_CreatedUtc",
            table: "RuntimeSnapshots",
            columns: ["DeviceId", "CreatedUtc"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Devices");
        migrationBuilder.DropTable(name: "RuntimeSnapshots");
        migrationBuilder.DropTable(name: "Scenarios");
        migrationBuilder.DropTable(name: "Settings");
    }
}
