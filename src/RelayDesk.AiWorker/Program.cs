using RelayDesk.AiWorker;
using RelayDesk.Modules.Ai;
using RelayDesk.Modules.KnowledgeBase;
using RelayDesk.Modules.Tickets;
using RelayDesk.SharedKernel.Ai;
using RelayDesk.SharedKernel.Messaging;
using RelayDesk.SharedKernel.Modules;
using RelayDesk.SharedKernel.Tenancy;

// AI worker (ADR 0002/0008): runs the slow, failure-prone AI work off the request path. Consumes events
// from RabbitMQ through a durable inbox, works inside each event's tenant (RLS applies; it connects as
// relay_app), and announces results through the outbox.
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTenancyCore();
builder.Services.AddModules(builder.Configuration,
    typeof(TicketsModule).Assembly,        // read-only ticket access for prompts
    typeof(KnowledgeBaseModule).Assembly,  // indexing and retrieval
    typeof(AiModule).Assembly);
builder.AddRelayAi();
builder.AddRelayWorkerMessaging("relay-ai", maxParallel: 2,
    typeof(KnowledgeBaseModule).Assembly, typeof(AiModule).Assembly);
KnowledgeBaseModule.AddAiWorkerServices(builder.Services);
AiModule.AddAiWorkerServices(builder.Services);
builder.Services.AddHostedService<ModelBootstrapper>();

builder.Build().Run();
