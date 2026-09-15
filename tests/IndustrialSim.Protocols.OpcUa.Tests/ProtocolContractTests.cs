using IndustrialSim.Core.Domain;
using IndustrialSim.Protocols.Abstractions;
using IndustrialSim.Protocols.OpcUa;
using Opc.Ua;
using Opc.Ua.Client;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace IndustrialSim.Protocols.OpcUa.Tests;

public class ProtocolContractTests
{
    [Fact]
    public void Endpoint_descriptor_normalizes_case_and_root_path()
    {
        var left = OpcUaEndpointDescriptor.Parse("opc.tcp://LOCALHOST:4840/");
        var right = OpcUaEndpointDescriptor.Parse("opc.tcp://localhost:4840");

        Assert.Equal(left, right);
        Assert.Equal("opc.tcp://localhost:4840", left.Endpoint);
        Assert.NotEqual(left, OpcUaEndpointDescriptor.Parse("opc.tcp://127.0.0.1:4840"));
    }

    [Theory]
    [InlineData("http://localhost:4840")]
    [InlineData("opc.tcp://localhost:4840/path?query=yes")]
    [InlineData("opc.tcp://localhost:4840/path#fragment")]
    [InlineData("opc.tcp://user@localhost:4840")]
    public void Endpoint_descriptor_rejects_unsupported_uris(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => OpcUaEndpointDescriptor.Parse(endpoint));
    }

    [Fact]
    public async Task Shared_server_lifecycle_reuses_listener_and_releases_it_after_last_member()
    {
        await using var manager = new OpcUaEndpointHostManager();
        var port = GetFreePort();
        var endpoint = $"opc.tcp://127.0.0.1:{port}";
        var first = new OpcUaAdapter(manager);
        var second = new OpcUaAdapter(manager);
        await first.StartAsync(Runtime("device-a"), new ProtocolOptions(endpoint, port));
        await second.StartAsync(Runtime("device-b"), new ProtocolOptions(endpoint, port));

        Assert.Equal(1, manager.HostCount);
        Assert.Equal(2, manager.MemberCount(endpoint));
        Assert.True(first.IsStandardOpcUaServer);
        Assert.True(second.IsStandardOpcUaServer);

        await first.StopAsync();
        Assert.Equal(1, manager.HostCount);
        Assert.Equal(1, manager.MemberCount(endpoint));
        using (var unavailable = new TcpListener(IPAddress.Loopback, port))
            Assert.Throws<SocketException>(() => unavailable.Start());

        await second.StopAsync();
        Assert.Equal(0, manager.HostCount);
        using var available = new TcpListener(IPAddress.Loopback, port);
        available.Start();
    }

    [Fact]
    public async Task Shared_server_lifecycle_rejects_a_different_endpoint_on_the_same_port()
    {
        await using var manager = new OpcUaEndpointHostManager();
        var port = GetFreePort();
        var first = new OpcUaAdapter(manager);
        var second = new OpcUaAdapter(manager);
        await first.StartAsync(Runtime("device-a"), new ProtocolOptions($"opc.tcp://127.0.0.1:{port}", port));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            second.StartAsync(Runtime("device-b"), new ProtocolOptions($"opc.tcp://localhost:{port}", port)));

        Assert.Contains("already hosts", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, manager.HostCount);
        await first.StopAsync();
    }

    [Fact]
    public async Task Shared_endpoint_exposes_and_routes_two_devices_without_state_cross_talk()
    {
        await using var manager = new OpcUaEndpointHostManager();
        var port = GetFreePort();
        var endpoint = $"opc.tcp://127.0.0.1:{port}";
        var runtimeA = Runtime("device-a", 1);
        var runtimeB = Runtime("device-b", 2);
        var first = new OpcUaAdapter(manager);
        var second = new OpcUaAdapter(manager);
        await first.StartAsync(runtimeA, new ProtocolOptions(endpoint, port));
        await second.StartAsync(runtimeB, new ProtocolOptions(endpoint, port));

        var config = await CreateClientConfigurationAsync();
        using var session = await CreateSessionAsync(config, port);
        var devices = await session.FetchReferencesAsync(new NodeId("industrial-sim/devices", 2), CancellationToken.None);
        Assert.Contains(devices, reference => reference.BrowseName.Name == "device-a");
        Assert.Contains(devices, reference => reference.BrowseName.Name == "device-b");
        var firstReferences = await session.FetchReferencesAsync(new NodeId("device-a", 2), CancellationToken.None);
        Assert.Contains(firstReferences, reference => reference.BrowseName.Name == "speed");

        var speedA = new NodeId("device-a/speed", 2);
        var speedB = new NodeId("device-b/speed", 2);
        Assert.Equal(1, Convert.ToInt32((await session.ReadValueAsync(speedA)).Value));
        Assert.Equal(2, Convert.ToInt32((await session.ReadValueAsync(speedB)).Value));
        var write = await session.WriteAsync(null, new WriteValueCollection
        {
            new() { NodeId = speedA, AttributeId = Attributes.Value, Value = new DataValue(new Variant(11)) }
        }, CancellationToken.None);
        Assert.True(StatusCode.IsGood(write.Results[0]));
        Assert.Equal(11, runtimeA.Read("speed")!.Value);
        Assert.Equal(2, runtimeB.Read("speed")!.Value);

        await session.CallAsync(new NodeId("device-a", 2), new NodeId("device-a/start", 2), CancellationToken.None);
        Assert.Equal(1, runtimeA.CommandsInvoked);
        Assert.Equal(0, runtimeB.CommandsInvoked);

        await first.StopAsync();
        var removed = await Assert.ThrowsAsync<ServiceResultException>(() => session.ReadValueAsync(speedA));
        Assert.Equal(StatusCodes.BadNodeIdUnknown, removed.StatusCode);
        Assert.Equal(2, Convert.ToInt32((await session.ReadValueAsync(speedB)).Value));
        await second.StopAsync();
    }

    [Fact]
    public async Task Shared_endpoint_rejects_node_id_collisions_without_removing_existing_device()
    {
        await using var manager = new OpcUaEndpointHostManager();
        var port = GetFreePort();
        var endpoint = $"opc.tcp://127.0.0.1:{port}";
        var first = new OpcUaAdapter(manager);
        first.Configure(new Dictionary<string, string> { ["speed"] = "shared/speed" });
        var second = new OpcUaAdapter(manager);
        second.Configure(new Dictionary<string, string> { ["speed"] = "shared/speed" });
        await first.StartAsync(Runtime("device-a"), new ProtocolOptions(endpoint, port));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            second.StartAsync(Runtime("device-b"), new ProtocolOptions(endpoint, port)));

        Assert.Contains("already registered", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, manager.MemberCount(endpoint));
        var config = await CreateClientConfigurationAsync();
        using var session = await CreateSessionAsync(config, port);
        Assert.Equal(0, Convert.ToInt32((await session.ReadValueAsync(new NodeId("shared/speed", 2))).Value));
        await first.StopAsync();
    }

    [Fact]
    public async Task Shared_endpoint_subscriptions_keep_device_notifications_isolated()
    {
        await using var manager = new OpcUaEndpointHostManager();
        var port = GetFreePort();
        var endpoint = $"opc.tcp://127.0.0.1:{port}";
        var runtimeA = Runtime("device-a");
        var runtimeB = Runtime("device-b");
        var first = new OpcUaAdapter(manager);
        var second = new OpcUaAdapter(manager);
        await first.StartAsync(runtimeA, new ProtocolOptions(endpoint, port));
        await second.StartAsync(runtimeB, new ProtocolOptions(endpoint, port));
        var config = await CreateClientConfigurationAsync();
        using var session = await CreateSessionAsync(config, port);

        var observed = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 50 };
        Assert.True(session.AddSubscription(subscription));
        await subscription.CreateAsync(CancellationToken.None);
        foreach (var node in new[] { "device-a/speed", "device-b/speed" })
        {
            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = new NodeId(node, 2),
                AttributeId = Attributes.Value,
                QueueSize = 10,
                DiscardOldest = true
            };
            item.Notification += (_, args) =>
            {
                if (args.NotificationValue is not MonitoredItemNotification notification) return;
                var value = Convert.ToInt32(notification.Value.Value);
                if ((node == "device-a/speed" && value == 101) || (node == "device-b/speed" && value == 202))
                    observed[node] = value;
                if (observed.Count == 2) received.TrySetResult();
            };
            subscription.AddItem(item);
        }
        await subscription.ApplyChangesAsync(CancellationToken.None);

        runtimeA.State.SetInternal(new DataPointId("speed"), 101);
        runtimeB.State.SetInternal(new DataPointId("speed"), 202);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(101, observed["device-a/speed"]);
        Assert.Equal(202, observed["device-b/speed"]);
        await first.StopAsync();
        await second.StopAsync();
    }

    [Fact]
    public async Task Shared_network_fault_affects_only_the_target_device_and_recovery_publishes_latest_state()
    {
        await using var manager = new OpcUaEndpointHostManager();
        var port = GetFreePort();
        var endpoint = $"opc.tcp://127.0.0.1:{port}";
        var runtimeA = Runtime("device-a", 1);
        var runtimeB = Runtime("device-b", 2);
        var first = new OpcUaAdapter(manager);
        var second = new OpcUaAdapter(manager);
        await first.StartAsync(runtimeA, new ProtocolOptions(endpoint, port));
        await second.StartAsync(runtimeB, new ProtocolOptions(endpoint, port));
        var config = await CreateClientConfigurationAsync();
        using var session = await CreateSessionAsync(config, port);
        var speedA = new NodeId("device-a/speed", 2);
        var speedB = new NodeId("device-b/speed", 2);

        var observedA = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedB = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 50 };
        Assert.True(session.AddSubscription(subscription));
        await subscription.CreateAsync(CancellationToken.None);
        foreach (var (node, expected, target) in new[]
        {
            (speedA, 9, observedA),
            (speedB, 8, observedB)
        })
        {
            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = node,
                AttributeId = Attributes.Value,
                QueueSize = 10,
                DiscardOldest = true
            };
            item.Notification += (_, args) =>
            {
                if (args.NotificationValue is MonitoredItemNotification notification && Convert.ToInt32(notification.Value.Value) == expected)
                    target.TrySetResult(expected);
            };
            subscription.AddItem(item);
        }
        await subscription.ApplyChangesAsync(CancellationToken.None);

        first.ApplyTransportFault("disconnect", TimeSpan.Zero);
        var disconnected = await Assert.ThrowsAsync<ServiceResultException>(() => session.ReadValueAsync(speedA));
        Assert.Equal(StatusCodes.BadNotConnected, disconnected.StatusCode);
        Assert.Equal(2, Convert.ToInt32((await session.ReadValueAsync(speedB)).Value));
        Assert.True(runtimeA.Write("speed", 9).Succeeded);
        Assert.True(runtimeB.Write("speed", 8).Succeeded);
        Assert.Equal(8, await observedB.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(observedA.Task.IsCompleted);

        first.RecoverTransportFault();
        Assert.Equal(9, await observedA.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(9, Convert.ToInt32((await session.ReadValueAsync(speedA)).Value));

        first.ApplyTransportFault("timeout", TimeSpan.FromMilliseconds(150));
        var stopwatch = Stopwatch.StartNew();
        Assert.Equal(8, Convert.ToInt32((await session.ReadValueAsync(speedB)).Value));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100));
        var timeout = await Assert.ThrowsAsync<ServiceResultException>(() => session.ReadValueAsync(speedA));
        Assert.Equal(StatusCodes.BadTimeout, timeout.StatusCode);
        first.RecoverTransportFault();

        await first.StopAsync();
        Assert.Equal(8, Convert.ToInt32((await session.ReadValueAsync(speedB)).Value));
        await second.StopAsync();
    }

    [Fact]
    public async Task Runtime_contract_supports_state_reads_writes_commands_and_events()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("pump-001"), "pump", new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0) }, new[] { new CommandDefinition("start") }));
        var changes = 0; runtime.State.DataPointChanged += _ => changes++;
        Assert.Equal(0, runtime.Read("speed")!.Value);
        Assert.True(runtime.Write("speed", 12).Succeeded);
        await runtime.InvokeCommandAsync("start");
        Assert.Equal(1, runtime.CommandsInvoked);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task OpcUa_maps_nodes_and_methods_to_runtime()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("pump-001"), "pump", new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0) }, new[] { new CommandDefinition("start") }));
        var adapter = new OpcUaAdapter(); await adapter.StartAsync(runtime, new ProtocolOptions()); adapter.Write("pump-001/speed", 8); Assert.Equal(8, adapter.Read("pump-001/speed")); await adapter.InvokeMethodAsync("pump-001/start"); Assert.Equal(1, runtime.CommandsInvoked);
    }

    [Fact]
    public async Task Transport_disconnect_does_not_stop_runtime()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("p"), "pump", new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.Read, 1) }));
        var adapter = new OpcUaAdapter(); await adapter.StartAsync(runtime, new ProtocolOptions()); adapter.ApplyTransportFault("disconnect", TimeSpan.Zero);
        Assert.Throws<IOException>(() => adapter.Read("speed")); Assert.True(adapter.IsRunning); adapter.RecoverTransportFault(); Assert.Equal(1, adapter.Read("speed"));
    }

    [Fact]
    public async Task Standard_server_accepts_client_and_exposes_runtime_nodes()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("pump-001"), "pump",
            new[] { new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, 0) },
            new[] { new CommandDefinition("start") }));
        var adapter = new OpcUaAdapter();
        var port = GetFreePort();
        await adapter.StartAsync(runtime, new ProtocolOptions($"opc.tcp://127.0.0.1:{port}", port));
        Assert.True(adapter.IsStandardOpcUaServer);

        var trust = Path.Combine(Path.GetTempPath(), "industrial-sim-opcua", "client-trust"); Directory.CreateDirectory(trust);
        var config = new ApplicationConfiguration { ApplicationName = "IndustrialSimTest", ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration { ApplicationCertificate = new CertificateIdentifier(), TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = trust }, TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = trust }, RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = trust }, AutoAcceptUntrustedCertificates = true }, TransportQuotas = new TransportQuotas(), ClientConfiguration = new ClientConfiguration() };
        await config.ValidateAsync(ApplicationType.Client);
        var ep = await CoreClientUtils.SelectEndpointAsync(config, $"opc.tcp://127.0.0.1:{port}", false, null!, CancellationToken.None);
        using var session = await new DefaultSessionFactory(null!).CreateAsync(config, new ConfiguredEndpoint(null, ep, EndpointConfiguration.Create(config)), false, "test", 60000, new UserIdentity(new AnonymousIdentityToken()), null, CancellationToken.None);
        var node = new NodeId("pump-001/speed", 2);
        Assert.Equal(0, Convert.ToInt32((await session.ReadValueAsync(node)).Value));
        var write = new WriteValue { NodeId = node, AttributeId = Attributes.Value, Value = new DataValue(new Variant(7)) };
        var writeResults = await session.WriteAsync(null, new WriteValueCollection { write }, CancellationToken.None);
        Assert.True(StatusCode.IsGood(writeResults.Results[0]));
        Assert.Equal(7, runtime.Read("speed")!.Value);
        var methods = await session.CallAsync(new NodeId("pump-001", 2), new NodeId("pump-001/start", 2), CancellationToken.None);
        Assert.NotNull(methods);
        await adapter.StopAsync();
        using var probe = new TcpListener(IPAddress.Loopback, port);
        probe.Start();
        probe.Stop();
    }

    [Fact]
    public async Task Wire_network_faults_affect_opcua_services_and_recovery_exposes_latest_runtime_state()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("sensor-001"), "sensor",
            new[] { new DataPointDefinition("value", DataType.Int32, DataPointAccess.ReadWrite, 1) }));
        var adapter = new OpcUaAdapter();
        var port = GetFreePort();
        await adapter.StartAsync(runtime, new ProtocolOptions($"opc.tcp://127.0.0.1:{port}", port));
        var config = await CreateClientConfigurationAsync();
        using var session = await CreateSessionAsync(config, port);
        var node = new NodeId("sensor-001/value", 2);

        adapter.ApplyTransportFault("latency", TimeSpan.FromMilliseconds(150));
        var stopwatch = Stopwatch.StartNew();
        Assert.Equal(1, Convert.ToInt32((await session.ReadValueAsync(node)).Value));
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(100));

        adapter.ApplyTransportFault("timeout", TimeSpan.FromMilliseconds(150));
        var timedOut = await Assert.ThrowsAsync<ServiceResultException>(() => session.ReadValueAsync(node));
        Assert.Equal(StatusCodes.BadTimeout, timedOut.StatusCode);

        adapter.ApplyTransportFault("disconnect", TimeSpan.Zero);
        await Assert.ThrowsAnyAsync<Exception>(() => session.ReadValueAsync(node));
        Assert.True(runtime.Write("value", 9).Succeeded);

        adapter.RecoverTransportFault();
        using var recovered = await CreateSessionAsync(config, port);
        Assert.Equal(9, Convert.ToInt32((await recovered.ReadValueAsync(node)).Value));
        Assert.True(adapter.IsRunning);
        await adapter.StopAsync();
    }

    [Fact]
    public async Task Standard_server_maps_int8_uint8_and_reports_runtime_events()
    {
        var runtime = new InMemoryDeviceRuntime(new DeviceDefinition(new DeviceId("scalar-001"), "sensor", new[]
        {
            new DataPointDefinition("signed", DataType.Int8, DataPointAccess.Read, (sbyte)-7),
            new DataPointDefinition("unsigned", DataType.UInt8, DataPointAccess.Read, (byte)250)
        }, new[] { new CommandDefinition("reset") }));
        var adapter = new OpcUaAdapter();
        var port = GetFreePort();
        await adapter.StartAsync(runtime, new ProtocolOptions($"opc.tcp://127.0.0.1:{port}", port));
        var config = await CreateClientConfigurationAsync();
        using var session = await CreateSessionAsync(config, port);

        var signed = Assert.IsAssignableFrom<VariableNode>(await session.ReadNodeAsync(new NodeId("scalar-001/signed", 2)));
        var unsigned = Assert.IsAssignableFrom<VariableNode>(await session.ReadNodeAsync(new NodeId("scalar-001/unsigned", 2)));
        Assert.Equal(DataTypeIds.SByte, signed.DataType);
        Assert.Equal(DataTypeIds.Byte, unsigned.DataType);

        var filter = new EventFilter();
        filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.Message);
        var messages = new ConcurrentQueue<string>();
        var received = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 50 };
        Assert.True(session.AddSubscription(subscription));
        await subscription.CreateAsync(CancellationToken.None);
        var monitored = new MonitoredItem(subscription.DefaultItem)
        {
            StartNodeId = ObjectIds.Server,
            AttributeId = Attributes.EventNotifier,
            QueueSize = 10,
            DiscardOldest = true,
            Filter = filter
        };
        monitored.Notification += (_, args) =>
        {
            if (args.NotificationValue is EventFieldList fields && fields.EventFields.FirstOrDefault().Value is LocalizedText message)
            {
                messages.Enqueue(message.Text);
                if (messages.Count >= 4) received.TrySetResult(messages.ToArray());
            }
        };
        subscription.AddItem(monitored);
        await subscription.ApplyChangesAsync(CancellationToken.None);

        runtime.Publish(new DeviceStarted(SimulationTime.Zero, runtime.Definition.Id));
        runtime.State.SetInternal(new DataPointId("signed"), (sbyte)-6);
        await runtime.InvokeCommandAsync("reset");
        runtime.Publish(new DeviceStopped(SimulationTime.Zero, runtime.Definition.Id));
        Assert.Equal(new[] { "DeviceStarted", "DataPointChanged:signed", "CommandExecuted:reset", "DeviceStopped" }, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await adapter.StopAsync();
    }

    private static async Task<ApplicationConfiguration> CreateClientConfigurationAsync()
    {
        var trust = Path.Combine(Path.GetTempPath(), "industrial-sim-opcua", "client-trust");
        Directory.CreateDirectory(trust);
        var config = new ApplicationConfiguration
        {
            ApplicationName = "IndustrialSimNetworkFaultTest",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier(),
                TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = trust },
                TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = trust },
                RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = trust },
                AutoAcceptUntrustedCertificates = true
            },
            TransportQuotas = new TransportQuotas { OperationTimeout = 1000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 1000 }
        };
        await config.ValidateAsync(ApplicationType.Client);
        return config;
    }

    private static async Task<ISession> CreateSessionAsync(ApplicationConfiguration config, int port)
    {
        var endpoint = await CoreClientUtils.SelectEndpointAsync(config, $"opc.tcp://127.0.0.1:{port}", false, null!, CancellationToken.None);
        return await new DefaultSessionFactory(null!).CreateAsync(config, new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(config)), false, "network-fault-test", 1000, new UserIdentity(new AnonymousIdentityToken()), null, CancellationToken.None);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static InMemoryDeviceRuntime Runtime(string id, int initial = 0) => new(new DeviceDefinition(
        new DeviceId(id),
        "custom",
        [new DataPointDefinition("speed", DataType.Int32, DataPointAccess.ReadWrite, initial)],
        [new CommandDefinition("start")]));
}
