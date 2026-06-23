using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace IronDev.Api.Auth;

/// <summary>Creates and validates JWTs for IronDev.</summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Issues a JWT. TenantId is null for a base (post-login) token.
    /// After the user selects a tenant, a new token is issued that includes tenant_id.
    /// </summary>
    string CreateToken(int userId, string email, string displayName, int? tenantId = null);
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly string _key;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiryMinutes;

    public JwtTokenService(IConfiguration configuration)
    {
        var section = configuration.GetSection("Jwt");
        _key = JwtSigningKeyResolver.Resolve(configuration);
        _issuer = section["Issuer"] ?? "irondev-api";
        _audience = section["Audience"] ?? "irondev-client";
        _expiryMinutes = int.TryParse(section["ExpiryMinutes"], out var exp) ? exp : 60;
    }

    public string CreateToken(int userId, string email, string displayName, int? tenantId = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new("display_name", displayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (tenantId.HasValue)
            claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public static class JwtSigningKeyResolver
{
    public const int MinimumSigningKeyLength = 32;
    public const string MissingSigningKeyMessage =
        "JWT signing key is not configured. Set Jwt__Key or IRONDEV_JWT_KEY outside committed appsettings.";
    public const string ShortSigningKeyMessage =
        "JWT signing key must be at least 32 characters.";

    public static string Resolve(IConfiguration configuration) =>
        Resolve(configuration, Environment.GetEnvironmentVariable);

    public static string Resolve(
        IConfiguration configuration,
        Func<string, string?> environmentVariableReader)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environmentVariableReader);

        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
            key = environmentVariableReader("IRONDEV_JWT_KEY");

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(MissingSigningKeyMessage);

        if (key.Length < MinimumSigningKeyLength)
            throw new InvalidOperationException(ShortSigningKeyMessage);

        return key;
    }
}
