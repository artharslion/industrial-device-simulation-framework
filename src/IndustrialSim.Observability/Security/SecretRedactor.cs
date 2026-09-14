using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace IndustrialSim.Observability.Security;

public sealed partial class SecretRedactor
{
    public const string RedactedValue = "[REDACTED]";

    private static readonly string[] SecretMarkers =
    [
        "password", "passwd", "pwd", "secret", "token", "accesstoken",
        "refreshtoken", "apikey", "authorization", "cookie",
        "connectionstring", "clientsecret", "privatekey"
    ];

    public bool IsSecretKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var normalized = new string(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return SecretMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    public string Redact(string key, string? value) =>
        IsSecretKey(key) ? RedactedValue : RedactText(value ?? string.Empty);

    public IReadOnlyDictionary<string, string> RedactMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        var redacted = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metadata is not null)
            foreach (var (key, value) in metadata.Take(64))
                redacted[key] = Limit(Redact(key, value));
        return new ReadOnlyDictionary<string, string>(redacted);
    }

    public object? RedactValue(string? key, object? value)
    {
        if (IsSecretKey(key)) return RedactedValue;
        return value is string text ? RedactText(text) : value;
    }

    public string RedactText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var result = BearerCredential().Replace(text, "Bearer " + RedactedValue);
        result = ConnectionCredential().Replace(result, match => $"{match.Groups[1].Value}={RedactedValue}");
        return UriUserInfo().Replace(result, match => $"{match.Groups[1].Value}{RedactedValue}@");
    }

    private static string Limit(string value) => value.Length <= 2048 ? value : value[..2048];

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex BearerCredential();

    [GeneratedRegex(@"\b(Password|Pwd|AccessToken|RefreshToken|ApiKey|ClientSecret)\s*=\s*([^;,\s]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex ConnectionCredential();

    [GeneratedRegex(@"(https?://)[^/@\s]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex UriUserInfo();
}
