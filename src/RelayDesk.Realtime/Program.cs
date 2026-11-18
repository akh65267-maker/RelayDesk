using Microsoft.AspNetCore.Authentication.JwtBearer;
using RelayDesk.Realtime;
using RelayDesk.SharedKernel.Messaging;
using RelayDesk.SharedKernel.Widget;
using StackExchange.Redis;

// Realtime service (ADR 0007): SignalR hubs for agents and widget visitors, scaled out with a Redis
// backplane. It consumes integration events from RabbitMQ and pushes notifications; it never writes
// domain data, and asks the API to check membership and conversation ownership.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRedisClient("redis");

var auth = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
auth.AddKeycloakJwtBearer("keycloak", realm: builder.Configuration["Keycloak:Realm"] ?? "relay", options =>
{
    options.Audience = "relay-api";
    options.MapInboundClaims = false;
    if (builder.Environment.IsDevelopment()) options.RequireHttpsMetadata = false;
    // Browsers can't set headers on WebSockets, so SignalR sends the token in the query string.
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Query.TryGetValue("access_token", out var t))
                ctx.Token = t;
            return Task.CompletedTask;
        },
    };
});
VisitorTokens.AddVisitorScheme(auth, builder.Configuration);
builder.Services.AddAuthorization();

// Clients authenticate with bearer tokens, not cookies, so any origin may connect (the widget runs on customers' sites).
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddSignalR().AddStackExchangeRedis(o =>
{
    o.Configuration = ConfigurationOptions.Parse(builder.Configuration.GetConnectionString("redis")
        ?? throw new InvalidOperationException("Connection string 'redis' is missing."));
    o.Configuration.ChannelPrefix = RedisChannel.Literal("relay-signalr");
});

builder.Services.AddHttpClient<ApiChecks>(c => c.BaseAddress = new Uri("https+http://api"));
builder.Services.AddSingleton<Presence>();

builder.AddRelayEventSubscriber("relay-realtime", typeof(RealtimeEventHandler).Assembly);

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapHub<AgentHub>("/hubs/agent");
app.MapHub<VisitorHub>("/hubs/visitor");
app.MapGet("/", () => Results.Ok(new { service = "relay-realtime" }));

app.Run();
