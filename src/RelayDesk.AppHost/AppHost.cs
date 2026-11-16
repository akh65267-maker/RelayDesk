var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17")
    .WithDataVolume();
var db = postgres.AddDatabase("relaydesk");

var redis = builder.AddRedis("redis");
var rabbit = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();

var api = builder.AddProject<Projects.RelayDesk_Api>("api")
    .WithReference(db).WaitFor(db)
    .WithReference(rabbit).WaitFor(rabbit)
    .WithReference(redis);

builder.AddProject<Projects.RelayDesk_Realtime>("realtime")
    .WithReference(redis).WaitFor(redis)
    .WithReference(rabbit).WaitFor(rabbit);

builder.AddProject<Projects.RelayDesk_AiWorker>("ai-worker")
    .WithReference(db).WaitFor(db)
    .WithReference(rabbit).WaitFor(rabbit);

builder.Build().Run();
