using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace RelayDesk.Modules.Tenancy.Keycloak;

internal sealed class KeycloakAdminOptions
{
    public const string Section = "Keycloak";

    /// <summary>Resolved through Aspire service discovery: prefers https, falls back to http.</summary>
    public string BaseUrl { get; set; } = "https+http://keycloak";
    public string Realm { get; set; } = "relay";
    public string BackendClientId { get; set; } = "relay-backend";
    public string BackendClientSecret { get; set; } = "";
}

/// <summary>Keeps Keycloak Organizations in sync with tenants (ADR 0005).</summary>
internal interface IKeycloakAdmin
{
    Task<string> CreateOrganizationAsync(string name, string alias, CancellationToken ct);
    Task AddMemberAsync(string organizationId, string userId, CancellationToken ct);
    Task DeleteOrganizationAsync(string organizationId, CancellationToken ct);
}

internal sealed class KeycloakAdminClient(HttpClient http, IOptions<KeycloakAdminOptions> options, TimeProvider clock)
    : IKeycloakAdmin
{
    private readonly KeycloakAdminOptions _o = options.Value;
    // Static: typed HttpClient instances are transient, the service-account token is shared.
    private static string? _token;
    private static DateTimeOffset _tokenExpires;

    public async Task<string> CreateOrganizationAsync(string name, string alias, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Post, "organizations", new { name, alias, enabled = true }, ct);
        await EnsureSuccess(res, ct);
        // Keycloak returns the new id only in the Location header.
        return res.Headers.Location?.Segments.Last()
            ?? throw new InvalidOperationException("Keycloak did not return the organization location.");
    }

    public async Task AddMemberAsync(string organizationId, string userId, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Post, $"organizations/{organizationId}/members", userId, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.Conflict) return; // already a member
        await EnsureSuccess(res, ct);
    }

    public async Task DeleteOrganizationAsync(string organizationId, CancellationToken ct)
    {
        using var res = await SendAsync(HttpMethod.Delete, $"organizations/{organizationId}", null, ct);
        if (res.StatusCode != System.Net.HttpStatusCode.NotFound) await EnsureSuccess(res, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var req = new HttpRequestMessage(method, $"{_o.BaseUrl.TrimEnd('/')}/admin/realms/{_o.Realm}/{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(ct));
        if (body is not null) req.Content = JsonContent.Create(body);
        return await http.SendAsync(req, ct);
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && clock.GetUtcNow() < _tokenExpires) return _token;

        using var res = await http.PostAsync(
            $"{_o.BaseUrl.TrimEnd('/')}/realms/{_o.Realm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _o.BackendClientId,
                ["client_secret"] = _o.BackendClientSecret,
            }), ct);
        await EnsureSuccess(res, ct);

        var token = await res.Content.ReadFromJsonAsync<TokenResponse>(ct)
            ?? throw new InvalidOperationException("Empty token response from Keycloak.");
        _token = token.AccessToken;
        _tokenExpires = clock.GetUtcNow().AddSeconds(token.ExpiresIn - 30);
        return _token;
    }

    private static async Task EnsureSuccess(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        var detail = await res.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"Keycloak admin call failed ({(int)res.StatusCode}): {detail}", null, res.StatusCode);
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
