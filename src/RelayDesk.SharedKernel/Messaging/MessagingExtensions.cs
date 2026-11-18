using System.Reflection;
using JasperFx;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using RelayDesk.SharedKernel.Persistence;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace RelayDesk.SharedKernel.Messaging;

public static class MessagingExtensions
{
    public const string WolverineSchema = "wolverine";

    /// <summary>
    /// Wolverine setup shared by hosts (ADR 0006): durable envelope storage in PostgreSQL, EF Core
    /// outbox integration, and durable local queues for in-process consumers. Retries, de-duplication
    /// (durable inbox) and dead-lettering come from Wolverine's storage.
    /// </summary>
    /// <param name="buildStorage">
    /// Only the MigrationService (schema owner) creates Wolverine's tables. Hosts that connect as
    /// relay_app must not try to change the schema.
    /// </param>
    public static IHostApplicationBuilder AddRelayMessaging(
        this IHostApplicationBuilder builder, bool buildStorage, params Assembly[] handlerAssemblies)
    {
        var cs = builder.Configuration.GetConnectionString(ModuleDbContextExtensions.ConnectionStringName)
            ?? throw new InvalidOperationException("Connection string 'relaydesk' is missing.");

        builder.UseWolverine(opts =>
        {
            opts.ServiceName = builder.Environment.ApplicationName;
            opts.PersistMessagesWithPostgresql(cs, WolverineSchema);
            opts.UseEntityFrameworkCoreTransactions();
            opts.Policies.UseDurableLocalQueues();
            opts.Policies.UseDurableInboxOnAllListeners();
            opts.AutoBuildMessageStorageOnStartup = buildStorage ? AutoCreate.CreateOrUpdate : AutoCreate.None;

            // Modules keep their implementations internal (ADR 0002), so Wolverine's generated
            // handler code can't construct them inline; let it resolve them from the container.
            opts.ServiceLocationPolicy = JasperFx.CodeGeneration.Model.ServiceLocationPolicy.AlwaysAllowed;

            // Transient failures are retried with backoff, then the message is dead-lettered
            // into wolverine_dead_letters, where it can be inspected and replayed.
            opts.OnException<Exception>()
                .RetryWithCooldown(TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5))
                .Then.MoveToErrorQueue();

            foreach (var assembly in handlerAssemblies)
                opts.Discovery.IncludeAssembly(assembly);
        });

        return builder;
    }
}
