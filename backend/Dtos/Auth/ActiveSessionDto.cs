namespace SayimLink.Api.Dtos.Auth;

public sealed class ActiveSessionDto
{
    public string Id { get; set; } = string.Empty;
    public string Browser { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public string? Ip { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsCurrent { get; set; }
}
