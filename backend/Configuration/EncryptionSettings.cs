namespace SayimLink.Api.Configuration;

public sealed class EncryptionSettings
{
    public const string SectionName = "Encryption";

    /// <summary>Base64-encoded 32-byte (256-bit) master key. Generate with
    /// <c>openssl rand -base64 32</c>. Required in production; if missing
    /// in Development a process-local ephemeral key is used and a warning
    /// is logged. Rotating this key invalidates every protected secret in
    /// the database (TOTP enrollments would need to be re-issued).</summary>
    public string MasterKey { get; set; } = string.Empty;
}
