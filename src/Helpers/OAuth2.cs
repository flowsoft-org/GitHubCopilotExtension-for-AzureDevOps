using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;

namespace Helpers;

public class OAuth2
{
    public static Dictionary<string, JsonWebKeySet> jwks_cache = new Dictionary<string, JsonWebKeySet>();

    public static string GetGitHubAuthorizationUrl(string clientId, string redirectUri, string scope)
    {
        return $"https://example.com/oauth/authorize?client_id={clientId}&redirect_uri={redirectUri}&scope={scope}&response_type=code";
    }

    public static string GetGitHubTokenUrl(string clientId, string clientSecret, string redirectUri, string code)
    {
        return $"https://example.com/oauth/token?client_id={clientId}&client_secret={clientSecret}&redirect_uri={redirectUri}&code={code}&grant_type=authorization_code";
    }

    public async static Task<OAuth2TokenResponse> ConvertToTokenResponse(HttpContent tokenResponse)
    {
        if (tokenResponse.Headers.ContentType?.MediaType == "application/json")
        {
            return await tokenResponse.ReadFromJsonAsync<OAuth2TokenResponse>()
               ?? throw new InvalidOperationException("Failed to read token response");
        } 
        if (tokenResponse.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded")
        {
            var content = await tokenResponse.ReadAsStringAsync();
            var queryParams = System.Web.HttpUtility.ParseQueryString(content);
            return new OAuth2TokenResponse(
                queryParams["access_token"].ToString() ?? string.Empty,
                queryParams["token_type"].ToString() ?? string.Empty,
                int.Parse(queryParams["expires_in"].ToString() ?? "0"),
                queryParams["refresh_token"].ToString() ?? string.Empty
            );
        } else {
            throw new UnsupportedContentTypeException($"Unsupported content type: {tokenResponse.Headers.ContentType?.MediaType}");
        }
    }

    public async static Task<JwtSecurityToken> ParseAndValidateIdToken(string idToken, string audience, string issuer)
    {
        var tokenHandler = new JwtSecurityTokenHandler();

        // Check if the string is a valid JWT
        if (!tokenHandler.CanReadToken(idToken))
        {
            throw new ArgumentException("Invalid id_token format.");
        }
        // Parse the token
        var jwtToken = tokenHandler.ReadJwtToken(idToken);
        
        if (!jwtToken.Header.TryGetValue("kid", out var kid))
        {
            throw new ArgumentException("Missing 'kid' in id_token.");
        }
        // Get the signing keys endpoint
        var signingKey = await GetSigningKeyAsync(issuer, kid.ToString());

        // Validate the token (e.g., signature, issuer, audience)
        // You need the signing key and validation parameters for this
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            IssuerSigningKey = signingKey // Use the signing key for validation
        };

        try
        {
            SecurityToken validatedToken;
            tokenHandler.ValidateToken(idToken, validationParameters, out validatedToken);
            return (JwtSecurityToken)validatedToken;
        }
        catch (Exception ex)
        {
            throw new SecurityTokenException("Token validation failed.", ex);
        }
    }

    public static async Task<string> GetJwksUriAsync(string issuer)
    {
        using var httpClient = new HttpClient();
        var openidconfigUri = $"{issuer}/.well-known/openid-configuration";
        var response = await httpClient.GetFromJsonAsync<OpenIdConnectConfiguration>(openidconfigUri);
        return response.JwksUri ?? throw new InvalidOperationException("JWKS URI not found.");;
    }

    public static async Task<JsonWebKeySet> GetSigningKeysAsync(string jwksUri)
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetStringAsync(jwksUri);

        // Parse the JWKS JSON response
        return new JsonWebKeySet(response);
    }

    public static async Task<SecurityKey> GetSigningKeyAsync(string issuer, string kid)
    {
        // Get the JWKS URI
        if (jwks_cache.TryGetValue(issuer, out var jwks)) {
            // if a new key is added to the public JWKS
            // there is a need to restart all instances of the AuthService to retrieve the new key from the public endpoint
            return jwks.Keys.FirstOrDefault(k => k.Kid == kid) ?? throw new InvalidOperationException($"Key with ID '{kid}' not found in JWKS.");
        } else {
            // Fetch the JWKS from the issuer
            jwks = await GetSigningKeysAsync(await GetJwksUriAsync(issuer));
            jwks_cache[issuer] = jwks;
            return await GetSigningKeyAsync(issuer, kid);
        }
    }
}

public record OAuth2TokenResponse(string access_token, string token_type, int expires_in, string refresh_token)
{
    public string AccessToken { get; init; } = access_token;
    public string TokenType { get; init; } = token_type;
    public int ExpiresIn { get; init; } = expires_in;
    public string RefreshToken { get; init; } = refresh_token;
}

public record OpenIdConnectConfiguration(string issuer_, string jwks_uri) {
    public string Issuer { get; init; } = issuer_;
    
    public string JwksUri { get; init; } = jwks_uri;
}