using IndustrialSim.Core.Domain;
using IndustrialSim.Faults;
using IndustrialSim.Hosting;
using Prometheus;

namespace IndustrialSim.Observability.Metrics;

public sealed class IndustrialSimMetrics
{
    private static readonly string[] Modes = ["deterministic", "realtime"];
    private static readonly string[] Actions = ["set", "ramp", "command", "wait", "fault", "unknown"];
    private static readonly string[] FaultCategories = ["data", "device", "network"];
    private static readonly string[] Protocols = ["opcua", "modbus", "other"];
    private static readonly string[] ProtocolOperations = ["start", "stop", "read", "write", "command", "other"];
    private static readonly string[] Streams = ["runtime-log", "signalr"];
    private static readonly string[] DropStages = ["ingress", "subscriber"];

    private readonly Counter _ticks;
    private readonly Counter _stateChanges;
    private readonly Counter _scenarioActions;
    private readonly Gauge _faultsActive;
    private readonly Gauge _protocolConnections;
    private readonly Counter _protocolErrors;
    private readonly Counter _streamDrops;
    private readonly object _tickGate = new();
    private readonly Dictionary<SimulationHost, long> _observedTicks = [];
    private ISimulationRegistry? _registry;

    public IndustrialSimMetrics(CollectorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var factory = Prometheus.Metrics.WithCustomRegistry(registry);
        _ticks = factory.CreateCounter(
            "industrial_simulation_ticks_total",
            "Simulation ticks completed without exporter work on the tick path.",
            new CounterConfiguration { LabelNames = ["mode"] });
        _stateChanges = factory.CreateCounter(
            "industrial_device_state_changes_total",
            "Published logical datapoint state changes.");
        _scenarioActions = factory.CreateCounter(
            "industrial_scenario_actions_total",
            "Completed scenario actions by bounded action category.",
            new CounterConfiguration { LabelNames = ["action"] });
        _faultsActive = factory.CreateGauge(
            "industrial_faults_active",
            "Active faults by bounded category.",
            new GaugeConfiguration { LabelNames = ["category"] });
        _protocolConnections = factory.CreateGauge(
            "industrial_protocol_connections",
            "Running protocol adapters by bounded protocol category.",
            new GaugeConfiguration { LabelNames = ["protocol"] });
        _protocolErrors = factory.CreateCounter(
            "industrial_protocol_errors_total",
            "Protocol lifecycle and operation failures by bounded categories.",
            new CounterConfiguration { LabelNames = ["protocol", "operation"] });
        _streamDrops = factory.CreateCounter(
            "industrial_stream_events_dropped_total",
            "Events rejected by bounded observation streams.",
            new CounterConfiguration { LabelNames = ["stream", "stage"] });

        InitializeSeries();
        registry.AddBeforeCollectCallback(CollectTicks);
    }

    public void Attach(ISimulationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        lock (_tickGate)
        {
            if (_registry is not null && !ReferenceEquals(_registry, registry))
                throw new InvalidOperationException("Metrics can observe only one simulation registry.");
            _registry = registry;
        }
    }

    public void Observe(object observation)
    {
        switch (observation)
        {
            case DataPointChanged:
                _stateChanges.Inc();
                break;
            case ScenarioActionObservation scenario:
                _scenarioActions.WithLabels(MetricLabels.ScenarioAction(scenario.Action)).Inc();
                break;
            case FaultEvent fault when fault.Lifecycle == FaultLifecycle.Active:
                _faultsActive.WithLabels(MetricLabels.FaultCategory(fault.Fault.Category)).Inc();
                break;
            case FaultEvent fault when fault.Lifecycle == FaultLifecycle.Recovered:
                _faultsActive.WithLabels(MetricLabels.FaultCategory(fault.Fault.Category)).Dec();
                break;
            case ProtocolLifecycleObservation protocol when protocol.Succeeded && protocol.Operation.Equals("start", StringComparison.OrdinalIgnoreCase):
                _protocolConnections.WithLabels(MetricLabels.Protocol(protocol.Protocol)).Inc();
                break;
            case ProtocolLifecycleObservation protocol when protocol.Succeeded && protocol.Operation.Equals("stop", StringComparison.OrdinalIgnoreCase):
                _protocolConnections.WithLabels(MetricLabels.Protocol(protocol.Protocol)).Dec();
                break;
            case ProtocolLifecycleObservation protocol when !protocol.Succeeded:
                _protocolErrors.WithLabels(
                    MetricLabels.Protocol(protocol.Protocol),
                    MetricLabels.ProtocolOperation(protocol.Operation)).Inc();
                break;
        }
    }

    public void RecordDroppedEvent(string stream, string stage) =>
        _streamDrops.WithLabels(MetricLabels.Stream(stream), MetricLabels.DropStage(stage)).Inc();

    private void CollectTicks()
    {
        lock (_tickGate)
        {
            if (_registry is null) return;
            var currentHosts = _registry.List().Select(summary => _registry.Get(summary.DeviceId).Host).ToArray();
            foreach (var host in currentHosts)
            {
                var total = host.TotalTicks;
                var previous = _observedTicks.GetValueOrDefault(host);
                if (total > previous)
                    _ticks.WithLabels(MetricLabels.Mode(host.IsDeterministic)).Inc(total - previous);
                _observedTicks[host] = total;
            }
            foreach (var removed in _observedTicks.Keys.Except(currentHosts).ToArray())
                _observedTicks.Remove(removed);
        }
    }

    private void InitializeSeries()
    {
        foreach (var mode in Modes) _ticks.WithLabels(mode).Inc(0);
        _stateChanges.Inc(0);
        foreach (var action in Actions) _scenarioActions.WithLabels(action).Inc(0);
        foreach (var category in FaultCategories) _faultsActive.WithLabels(category).Set(0);
        foreach (var protocol in Protocols) _protocolConnections.WithLabels(protocol).Set(0);
        foreach (var protocol in Protocols)
            foreach (var operation in ProtocolOperations)
                _protocolErrors.WithLabels(protocol, operation).Inc(0);
        foreach (var stream in Streams)
            foreach (var stage in DropStages)
                _streamDrops.WithLabels(stream, stage).Inc(0);
    }
}
