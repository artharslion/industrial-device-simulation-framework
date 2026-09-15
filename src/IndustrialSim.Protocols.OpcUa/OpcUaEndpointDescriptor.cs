namespace IndustrialSim.Protocols.OpcUa;

public sealed record OpcUaEndpointDescriptor(
    string Scheme,
    string Host,
    int Port,
    string Path,
    string Endpoint)
{
    public static OpcUaEndpointDescriptor Parse(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("OPC UA endpoint cannot be blank.", nameof(endpoint));
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("opc.tcp", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.Port is < 1 or > 65535)
            throw new ArgumentException($"OPC UA endpoint '{endpoint}' must be an absolute opc.tcp URI with a valid host and port.", nameof(endpoint));
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException($"OPC UA endpoint '{endpoint}' cannot contain credentials, a query, or a fragment.", nameof(endpoint));

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (path == "/") path = string.Empty;
        else path = path.TrimEnd('/');
        var normalized = $"{scheme}://{HostForUri(host)}:{uri.Port}{path}";
        return new OpcUaEndpointDescriptor(scheme, host, uri.Port, path, normalized);
    }

    private static string HostForUri(string host) => host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host;
}
