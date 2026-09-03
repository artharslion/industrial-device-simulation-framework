namespace IndustrialSim.Persistence.Tests;

public sealed class PersistenceBoundaryTests
{
    [Fact]
    public void Persistence_project_is_the_sqlite_implementation_boundary()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var project = Path.Combine(root, "src", "IndustrialSim.Persistence", "IndustrialSim.Persistence.csproj");
        Assert.True(File.Exists(project), $"Missing persistence project: {project}");

        var content = File.ReadAllText(project);
        Assert.Contains("Microsoft.EntityFrameworkCore.Sqlite", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IndustrialSim.Application", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IndustrialSim.Web", content, StringComparison.OrdinalIgnoreCase);
    }
}
