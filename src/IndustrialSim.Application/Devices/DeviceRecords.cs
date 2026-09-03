namespace IndustrialSim.Application.Devices;

public sealed record DeviceOperationResult(string DeviceId, bool Succeeded, string? Error = null);
