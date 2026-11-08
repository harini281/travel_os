namespace TravelOS.PlatformApi.Auth;

public sealed record RegisterRequest(string Email, string DisplayName, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, AuthUserResponse User);
public sealed record AuthUserResponse(Guid Id, string Email, string DisplayName, IReadOnlyCollection<string> Roles);
