namespace Attic.Contracts.Auth;

public sealed record RegisterRequest(string Email, string Username, string Password);
