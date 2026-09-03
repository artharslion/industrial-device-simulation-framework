using System.Reflection;
using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.State;

namespace IndustrialSim.Application.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void Core_and_runtime_do_not_reference_control_plane_or_concrete_protocols()
    {
        string[] forbidden =
        [
            "IndustrialSim.Application", "IndustrialSim.Persistence", "IndustrialSim.Web",
            "IndustrialSim.Protocols.Modbus", "IndustrialSim.Protocols.OpcUa"
        ];

        AssertNoReferences(typeof(DeviceDefinition).Assembly, forbidden);
        AssertNoReferences(typeof(StateStore).Assembly, forbidden);
    }

    [Fact]
    public void Application_project_exists_without_web_or_ef_dependencies()
    {
        var root = RepositoryRoot();
        var project = Path.Combine(root, "src", "IndustrialSim.Application", "IndustrialSim.Application.csproj");
        Assert.True(File.Exists(project), $"Missing application project: {project}");

        var content = File.ReadAllText(project);
        Assert.DoesNotContain("Microsoft.AspNetCore", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EntityFrameworkCore", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IndustrialSim.Web", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IndustrialSim.Persistence", content, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertNoReferences(Assembly assembly, IEnumerable<string> forbidden)
    {
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        foreach (var name in forbidden)
            Assert.DoesNotContain(name, references, StringComparer.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
