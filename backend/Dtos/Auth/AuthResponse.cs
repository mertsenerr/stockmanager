namespace SayimLink.Api.Dtos.Auth;

public sealed class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
    public UserDto User { get; set; } = new();
}
