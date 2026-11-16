using RelayDesk.Modules.Tenancy;
using RelayDesk.SharedKernel.Modules;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var modules = builder.Services.AddModules(builder.Configuration, typeof(TenancyModule).Assembly);

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapModules(modules);
app.MapGet("/", () => Results.Ok(new { service = "relay-api" }));

app.Run();

public partial class Program;
