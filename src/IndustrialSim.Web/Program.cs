using IndustrialSim.Core.Domain;
using IndustrialSim.Runtime.Engine;
using IndustrialSim.Runtime.State;
using IndustrialSim.Runtime.Time;

var builder = WebApplication.CreateBuilder(args);
var definition = new DeviceDefinition(new DeviceId("pump-001"), "pump", new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0), new DataPointDefinition("running", DataType.Boolean, DataPointAccess.Read, false), new DataPointDefinition("alarm", DataType.Boolean, DataPointAccess.Read, false) });
var state = new StateStore(definition); var engine = new SimulationEngine(new DeterministicClock());
var events = new List<RuntimeEvent>(); state.DataPointChanged += e => events.Add(e);
var app = builder.Build();
app.MapGet("/api/state", () => Results.Ok(state.Snapshot().ToDictionary(x => x.Key, x => x.Value?.Value)));
app.MapGet("/api/runtime", () => Results.Ok(new { state = engine.State.ToString(), time = engine.CurrentTime.Elapsed }));
app.MapGet("/api/events", () => Results.Ok(events.ToArray()));
app.MapPost("/api/runtime/{command}", async (string command) => { switch (command.ToLowerInvariant()) { case "start": await engine.StartAsync(); break; case "stop": await engine.StopAsync(); break; case "pause": engine.Pause(); break; case "reset": engine.Reset(); break; default: return Results.NotFound(); } return Results.Ok(new { state = engine.State.ToString() }); });
app.MapPost("/api/state/{name}", (string name, object value) => Results.Ok(state.Set(new DataPointId(name), value)));
app.MapGet("/", () => Results.Content("IndustrialSim Developer Console", "text/plain"));
app.Run();

public partial class Program { }
