namespace IndustrialSim.Core.Domain;

public readonly record struct SimulationTime(TimeSpan Elapsed) : IComparable<SimulationTime>
{
    public static SimulationTime Zero => new(TimeSpan.Zero);

    public static SimulationTime FromSeconds(double seconds) =>
        new(TimeSpan.FromSeconds(seconds));

    public int CompareTo(SimulationTime other) => Elapsed.CompareTo(other.Elapsed);
    public static SimulationTime operator +(SimulationTime left, TimeSpan right) => new(left.Elapsed + right);
    public static SimulationTime operator -(SimulationTime left, TimeSpan right) => new(left.Elapsed - right);
    public static bool operator <(SimulationTime left, SimulationTime right) => left.Elapsed < right.Elapsed;
    public static bool operator >(SimulationTime left, SimulationTime right) => left.Elapsed > right.Elapsed;
    public static bool operator <=(SimulationTime left, SimulationTime right) => left.Elapsed <= right.Elapsed;
    public static bool operator >=(SimulationTime left, SimulationTime right) => left.Elapsed >= right.Elapsed;
}
