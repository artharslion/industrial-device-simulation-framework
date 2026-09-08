using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using IndustrialSim.Core.Domain;
using IndustrialSim.Hosting;
using IndustrialSim.Hosting.Snapshots;
using IndustrialSim.Web;
using IndustrialSim.Web.Api.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndustrialSim.Web.Tests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task Disabled_mode_preserves_local_development_access()
    {
        await using var fixture = await AuthFixture.StartAsync("Disabled");
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync("/api/v1/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PostAsync("/api/v1/devices/auth-device/start", null)).StatusCode);
    }

    [Fact]
    public async Task Bootstrap_is_one_time_and_login_and_password_change_issue_valid_tokens()
    {
        await using var fixture = await AuthFixture.StartAsync("LocalIdentity");
        var password = "Initial!Pass123";
        var anonymous = await fixture.Client.GetAsync("/api/v1/devices");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Contains("authenticationRequired", await anonymous.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var bootstrap = await fixture.Client.PostAsJsonAsync("/api/v1/auth/bootstrap", new { userName = "admin", password });
        Assert.Equal(HttpStatusCode.Created, bootstrap.StatusCode);
        Assert.DoesNotContain(password, await bootstrap.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.Client.PostAsJsonAsync("/api/v1/auth/bootstrap", new { userName = "second", password })).StatusCode);

        var token = await fixture.LoginAsync("admin", password);
        Assert.DoesNotContain(fixture.Logs, message => message.Contains(token, StringComparison.Ordinal));
        Assert.DoesNotContain(password, fixture.SnapshotJson(), StringComparison.Ordinal);
        Assert.DoesNotContain(token, fixture.SnapshotJson(), StringComparison.Ordinal);
        fixture.Authorize(token);
        var change = await fixture.Client.PostAsJsonAsync("/api/v1/auth/password", new { currentPassword = password, newPassword = "Changed!Pass456" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        fixture.Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin", password })).StatusCode);
        Assert.NotEmpty(await fixture.LoginAsync("admin", "Changed!Pass456"));
    }

    [Fact]
    public async Task Viewer_operator_and_admin_permissions_are_enforced()
    {
        await using var fixture = await AuthFixture.StartAsync("LocalIdentity");
        await fixture.Client.PostAsJsonAsync("/api/v1/auth/bootstrap", new { userName = "admin", password = "Admin!Pass123" });
        fixture.Authorize(await fixture.LoginAsync("admin", "Admin!Pass123"));
        Assert.Equal(HttpStatusCode.Created, (await fixture.Client.PostAsJsonAsync("/api/v1/users", new { userName = "viewer", password = "Viewer!Pass123", role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await fixture.Client.PostAsJsonAsync("/api/v1/users", new { userName = "operator", password = "Operator!Pass123", role = "Operator" })).StatusCode);

        fixture.Authorize(await fixture.LoginAsync("viewer", "Viewer!Pass123"));
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync("/api/v1/devices/auth-device/state")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync("/api/v1/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Client.PostAsJsonAsync("/api/v1/templates", new { })).StatusCode);
        var forbidden = await fixture.Client.PostAsync("/api/v1/devices/auth-device/start", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Contains("forbidden", await forbidden.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Client.PostAsync("/api/runtime/start", null)).StatusCode);

        fixture.Authorize(await fixture.LoginAsync("operator", "Operator!Pass123"));
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PostAsync("/api/v1/devices/auth-device/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Client.GetAsync("/api/v1/users")).StatusCode);

        fixture.Authorize(await fixture.LoginAsync("admin", "Admin!Pass123"));
        var users = await fixture.Client.GetStringAsync("/api/v1/users");
        Assert.Contains("viewer", users, StringComparison.Ordinal);
        Assert.DoesNotContain("passwordHash", users, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", users, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NoContent, (await fixture.Client.PutAsJsonAsync("/api/v1/users/operator/role", new { role = "Viewer" })).StatusCode);
        Assert.Contains("Viewer", await fixture.Client.GetStringAsync("/api/v1/users"), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, (await fixture.Client.DeleteAsync("/api/v1/users/viewer")).StatusCode);
        var selfDelete = await fixture.Client.DeleteAsync("/api/v1/users/admin");
        Assert.Equal(HttpStatusCode.Conflict, selfDelete.StatusCode);
        Assert.Contains("cannotDeleteCurrentUser", await selfDelete.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PutAsJsonAsync("/api/v1/settings/ui.refreshSeconds", new { value = 2 })).StatusCode);
        var secret = await fixture.Client.PutAsJsonAsync("/api/v1/settings/apiToken", new { value = "do-not-store" });
        Assert.Equal(HttpStatusCode.BadRequest, secret.StatusCode);
        Assert.DoesNotContain("do-not-store", await secret.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_credentials_return_generic_problem_details_without_secrets()
    {
        await using var fixture = await AuthFixture.StartAsync("LocalIdentity");
        await fixture.Client.PostAsJsonAsync("/api/v1/auth/bootstrap", new { userName = "admin", password = "Admin!Pass123" });
        var response = await fixture.Client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin", password = "wrong-password" });
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalidCredentials", body, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong-password", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.Logs, message => message.Contains("wrong-password", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Settings_support_types_versions_and_sensitive_key_rejection()
    {
        await using var fixture = await AuthFixture.StartAsync("LocalIdentity");
        await fixture.Client.PostAsJsonAsync("/api/v1/auth/bootstrap", new { userName = "admin", password = "Admin!Pass123" });
        fixture.Authorize(await fixture.LoginAsync("admin", "Admin!Pass123"));

        var created = await fixture.Client.PutAsJsonAsync("/api/v1/settings/ui.refreshSeconds", new { type = "Number", value = 3, version = 0 });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using (var value = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Number", value.RootElement.GetProperty("type").GetString());
            Assert.Equal(1, value.RootElement.GetProperty("version").GetInt64());
        }
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PutAsJsonAsync("/api/v1/settings/ui.refreshSeconds", new { type = "Number", value = 5, version = 1 })).StatusCode);
        var stale = await fixture.Client.PutAsJsonAsync("/api/v1/settings/ui.refreshSeconds", new { type = "Number", value = 8, version = 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("persistenceConflict", await stale.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.PutAsJsonAsync("/api/v1/settings/privateKey", new { type = "String", value = "hidden" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync("/api/v1/settings/effective")).StatusCode);
    }

    private sealed class AuthFixture(WebApplication app, HttpClient client, string databasePath, SimulationRegistry registry, CapturingLoggerProvider logs) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public IReadOnlyCollection<string> Logs => logs.Messages;

        public static async Task<AuthFixture> StartAsync(string mode)
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"industrial-sim-auth-{Guid.NewGuid():N}.db");
            var builder = WebApplication.CreateBuilder();
            var logs = new CapturingLoggerProvider();
            builder.Logging.AddProvider(logs);
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            var registry = new SimulationRegistry();
            await registry.CreateAsync(new DeviceLaunchDefinition(
                new DeviceDefinition(new DeviceId("auth-device"), "custom",
                    [new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0)], [], []),
                new SimulationHostOptions(true, 1)));
            builder.Services.AddIndustrialSimControlPlane(registry, $"Data Source={databasePath};Pooling=False", mode);
            var app = builder.Build();
            app.UseIndustrialSimProblemDetails();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapIndustrialSimApi(registry.Get("auth-device").Host, requireAuthorization: true);
            app.MapIndustrialSimIdentity();
            app.MapIndustrialSimV1Api();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new AuthFixture(app, new HttpClient { BaseAddress = new Uri(address) }, databasePath, registry, logs);
        }

        public async Task<string> LoginAsync(string userName, string password)
        {
            var response = await Client.PostAsJsonAsync("/api/v1/auth/login", new { userName, password });
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("accessToken").GetString()!;
        }

        public void Authorize(string token) => Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        public string SnapshotJson() => JsonSerializer.Serialize(
            new RuntimeSnapshotService().Capture(registry.Get("auth-device").Host, "security-check"));

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            await registry.DisposeAsync();
            File.Delete(databasePath);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }
}
