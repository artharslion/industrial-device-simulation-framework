using IndustrialSim.Web;

var builder = WebApplication.CreateBuilder(args);
var configuredPath = Environment.GetEnvironmentVariable("INDUSTRIALSIM_DEVICE_CONFIG");
var simulation = await WebHostComposition.CreateAsync(configuredPath, builder.Environment.IsDevelopment());
builder.Services.AddSingleton(simulation);

var app = builder.Build();
await simulation.StartAsync(app.Lifetime.ApplicationStopping);

app.MapIndustrialSimApi(simulation);
app.MapGet("/", () => Results.Content("IndustrialSim Developer Console", "text/plain"));

try
{
    await app.RunAsync();
}
finally
{
    await simulation.DisposeAsync();
}

public partial class Program { }
