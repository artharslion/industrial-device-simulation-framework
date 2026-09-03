namespace IndustrialSim.Web.Api.V1;

public sealed record IndustrialAuthOptions
{
    public IndustrialAuthOptions(string mode)
    {
        if (!mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("LocalIdentity", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Auth mode must be Disabled or LocalIdentity.", nameof(mode));
        Mode = mode;
    }

    public string Mode { get; }
    public bool IsDisabled => Mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
}
