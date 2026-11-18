using RelayDesk.Api;
using RelayDesk.Modules.Audit;
using RelayDesk.Modules.Tenancy;
using RelayDesk.Modules.Tickets;
using RelayDesk.SharedKernel.Messaging;
using RelayDesk.SharedKernel.Modules;
using RelayDesk.SharedKernel.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
// Enums (e.g. roles) go over the wire as names: "Owner", not 3.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddTenancyCore();

builder.Services.AddAuthentication()
    .AddKeycloakJwtBearer("keycloak", realm: builder.Configuration["Keycloak:Realm"] ?? "relay", options =>
    {
        options.Audience = "relay-api";
        options.MapInboundClaims = false; // keep "sub" and "email" as-is
        if (builder.Environment.IsDevelopment()) options.RequireHttpsMetadata = false;
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var modules = builder.Services.AddModules(builder.Configuration,
    typeof(TenancyModule).Assembly,
    typeof(AuditModule).Assembly,
    typeof(TicketsModule).Assembly);

// Outbox + durable in-process event handling (ADR 0006). Storage is created by the MigrationService.
builder.AddRelayMessaging(buildStorage: false, typeof(AuditModule).Assembly);

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapModules(modules);
app.MapGet("/", () => Results.Ok(new { service = "relay-api" }));

app.Run();

public partial class Program;
