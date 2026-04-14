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
        _key = section["Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
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
