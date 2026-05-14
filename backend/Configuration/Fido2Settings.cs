namespace SayimLink.Api.Configuration;

public sealed class Fido2Settings
{
    /// <summary>Display name shown on the authenticator UI (Touch ID prompt etc.).</summary>
    public string ServerName { get; set; } = "SynCompare";

    /// <summary>Relying-party id — the bare host (no scheme/port).</summary>
    public string ServerDomain { get; set; } = "localhost";

    /// <summary>Origins permitted for ceremony (scheme + host + port).</summary>
    public string[] Origins { get; set; } = ["http://localhost:4200", "https://localhost:4200"];
}
