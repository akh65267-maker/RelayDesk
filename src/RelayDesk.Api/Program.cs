using Microsoft.AspNetCore.Authentication.JwtBearer;
using RelayDesk.Api;
using RelayDesk.Modules.Audit;
using RelayDesk.Modules.Tenancy;
using RelayDesk.Modules.Tickets;
using RelayDesk.SharedKernel.Messaging;
using RelayDesk.SharedKernel.Modules;
using RelayDesk.SharedKernel.Tenancy;
using RelayDesk.SharedKernel.Widget;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
// Enums (e.g. roles) go over the wire as names: "Owner", not 3.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddTenancyCore();

// Keycloak (agents) is the default scheme. Widget visitors use their own "Visitor" scheme, which only
// the widget endpoints opt into, so a visitor token can never reach agent endpoints.
var auth = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
auth.AddKeycloakJwtBearer("keycloak", realm: builder.Configuration["Keycloak:Realm"] ?? "relay", options =>
    {
        options.Audience = "relay-api";
        options.MapInboundClaims = false; // keep "sub" and "email" as-is
        if (builder.Environment.IsDevelopment()) options.RequireHttpsMetadata = false;
    });
VisitorTokens.AddVisitorScheme(auth, builder.Configuration);
builder.Services.AddAuthorization();

builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p => p
        .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod());
    // The widget is embedded on customers' sites. It sends a bearer token, never cookies, so any origin is safe.
    o.AddPolicy(VisitorTokens.CorsPolicy, p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

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
