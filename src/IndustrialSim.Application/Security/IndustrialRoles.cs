namespace IndustrialSim.Application.Security;

public static class IndustrialRoles
{
    public const string Viewer = "Viewer";
    public const string Operator = "Operator";
    public const string Admin = "Admin";
    public static readonly IReadOnlyList<string> All = [Viewer, Operator, Admin];
}

public static class IndustrialPolicies
{
    public const string Viewer = "IndustrialSim.Viewer";
    public const string Operator = "IndustrialSim.Operator";
    public const string Admin = "IndustrialSim.Admin";
}
