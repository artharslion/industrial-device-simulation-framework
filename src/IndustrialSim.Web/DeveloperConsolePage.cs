namespace IndustrialSim.Web;

public static class DeveloperConsolePage
{
    public static WebApplication MapIndustrialSimDeveloperConsole(this WebApplication app)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
        return app;
    }
}
