var builder = DistributedApplication.CreateBuilder(args);

// Generated once and persisted in user secrets.
var pgAppPassword = builder.AddParameter("pg-app-password",
    new GenerateParameterDefault { MinLength = 24, Special = false }, secret: true, persist: true);
var kcBackendSecret = builder.AddParameter("keycloak-backend-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

var webAuthSecret = builder.AddParameter("web-auth-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
var widgetSigningKey = builder.AddParameter("widget-signing-key",
    new GenerateParameterDefault { MinLength = 48, Special = false }, secret: true, persist: true);

const string webUrl = "http://localhost:3000";

var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithDataVolume()
    .WithEnvironment("POSTGRES_APP_PASSWORD", pgAppPassword)
    .WithInitFiles("../../deploy/postgres/init");
var db = postgres.AddDatabase("relaydesk");

// The API connects as relay_app (not owner, no BYPASSRLS) so RLS is enforced in development too.
var pgEndpoint = postgres.Resource.PrimaryEndpoint;
var appConnection = ReferenceExpression.Create(
    $"Host={pgEndpoint.Property(EndpointProperty.Host)};Port={pgEndpoint.Property(EndpointProperty.Port)};Database=relaydesk;Username=relay_app;Password={pgAppPassword}");

var redis = builder.AddRedis("redis");
var rabbit = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume()
    .WithRealmImport("../../deploy/keycloak")
    .WithEnvironment("RELAY_BACKEND_SECRET", kcBackendSecret)
    .WithEnvironment("RELAY_WEB_URL", webUrl)
    // Raises Keycloak's request header limit; see the file for why (HTTP 431 with localhost cookies).
    .WithBindMount("../../deploy/keycloak/quarkus.properties", "/opt/keycloak/conf/quarkus.properties", isReadOnly: true);

var migrations = builder.AddProject<Projects.RelayDesk_MigrationService>("migrations")
    .WithReference(db).WaitFor(db); // schema owner

var api = builder.AddProject<Projects.RelayDesk_Api>("api")
    .WithEnvironment("ConnectionStrings__relaydesk", appConnection)
    .WaitForCompletion(migrations)
    .WithReference(keycloak).WaitFor(keycloak)
    .WithEnvironment("Keycloak__BackendClientSecret", kcBackendSecret)
    .WithEnvironment("Cors__Origins__0", webUrl)
    .WithEnvironment("Widget__SigningKey", widgetSigningKey)
    .WithReference(rabbit).WaitFor(rabbit)
    .WithReference(redis);

// Validates agent (Keycloak) and visitor tokens itself, and asks the API to check membership.
var realtime = builder.AddProject<Projects.RelayDesk_Realtime>("realtime")
    .WithReference(keycloak).WaitFor(keycloak)
    .WithReference(api).WaitFor(api)
    .WithEnvironment("Widget__SigningKey", widgetSigningKey)
    .WithReference(redis).WaitFor(redis)
    .WithReference(rabbit).WaitFor(rabbit);

builder.AddProject<Projects.RelayDesk_AiWorker>("ai-worker")
    .WithReference(db).WaitFor(db)
    .WithReference(rabbit).WaitFor(rabbit);

builder.AddJavaScriptApp("web", "../../web", "dev")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithEnvironment("RELAY_API_URL", api.GetEndpoint("http"))
    // URLs the browser uses directly: SignalR for the live inbox, and the widget on the demo page.
    .WithEnvironment("RELAY_REALTIME_PUBLIC_URL", realtime.GetEndpoint("http"))
    .WithEnvironment("RELAY_API_PUBLIC_URL", api.GetEndpoint("http"))
    .WithEnvironment("AUTH_SECRET", webAuthSecret)
    .WithEnvironment("AUTH_KEYCLOAK_ISSUER", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/relay"))
    .WithEnvironment("AUTH_KEYCLOAK_ID", "relay-web")
    .WithEnvironment("AUTH_URL", webUrl)
    // File watching does not see edits made from Windows on /mnt/d under WSL; poll instead.
    .WithEnvironment("WATCHPACK_POLLING", "true")
    .WaitFor(api);

builder.Build().Run();
