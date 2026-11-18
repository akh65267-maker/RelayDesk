using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using RelayDesk.Modules.Tenancy.Keycloak;
using RelayDesk.SharedKernel.Persistence;
using Testcontainers.PostgreSql;

namespace RelayDesk.IntegrationTests.Infrastructure;

/// <summary>
/// Real PostgreSQL (with RLS) + the real API. Migrations run as the owner; the API connects as relay_app,
/// exactly like production. Keycloak is faked; authentication uses test headers.
/// </summary>
public sealed class RelayApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppPassword = "app-test-password";

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("relaydesk")
        .Build();

    public string OwnerConnectionString => _pg.GetConnectionString();

    public string AppConnectionString => new NpgsqlConnectionStringBuilder(_pg.GetConnectionString())
    {
        Username = RlsSql.AppRole,
        Password = AppPassword,
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();

        await using (var conn = new NpgsqlConnection(OwnerConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                $"CREATE ROLE {RlsSql.AppRole} LOGIN PASSWORD '{AppPassword}' NOSUPERUSER NOBYPASSRLS", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // Same path as the MigrationService: every module, as the schema owner.
        await MigratorHost.MigrateAsync(OwnerConnectionString);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _pg.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:relaydesk", AppConnectionString);
        builder.UseSetting("Logging:LogLevel:Default", "Warning");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.Configure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
            services.AddSingleton<FakeKeycloakAdmin>();
            services.AddSingleton<IKeycloakAdmin>(sp => sp.GetRequiredService<FakeKeycloakAdmin>());
        });
    }

    /// <summary>A client acting as the given user. Pass a tenant to send X-Tenant-Id.</summary>
    public HttpClient ClientFor(TestUser user, Guid? tenantId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user.Id);
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, user.Email);
        if (tenantId is not null) client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        return client;
    }
}

public sealed record TestUser(string Id, string Email)
{
    public static TestUser New(string name) => new(Guid.NewGuid().ToString(), $"{name}-{Guid.NewGuid():N}@example.test");
}

internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string EmailHeader = "X-Test-Email";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var sub))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("sub", sub!) };
        if (Request.Headers.TryGetValue(EmailHeader, out var email)) claims.Add(new("email", email!));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

internal sealed class FakeKeycloakAdmin : IKeycloakAdmin
{
    private readonly HashSet<string> _aliases = [];

    public Task<string> CreateOrganizationAsync(string name, string alias, CancellationToken ct)
    {
        lock (_aliases)
        {
            if (!_aliases.Add(alias))
                throw new HttpRequestException("conflict", null, System.Net.HttpStatusCode.Conflict);
        }
        return Task.FromResult(Guid.NewGuid().ToString());
    }

    public Task AddMemberAsync(string organizationId, string userId, CancellationToken ct) => Task.CompletedTask;

    public Task DeleteOrganizationAsync(string organizationId, CancellationToken ct) => Task.CompletedTask;
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<RelayApiFactory>
{
    public const string Name = "api";
}
