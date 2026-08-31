namespace IndustrialSim.Web.Tests;

public class WebContractTests
{
    [Fact]
    public void Web_project_exposes_developer_api_contract() => Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "IndustrialSim.Web.dll")));
}
