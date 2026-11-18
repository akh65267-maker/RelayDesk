using System.Reflection;
using JasperFx;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using RelayDesk.Contracts;
using RelayDesk.SharedKernel.Persistence;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace RelayDesk.SharedKernel.Messaging;

public static class MessagingExtensions
{
    public const string WolverineSchema = "wolverine";

    /// <summary>Fanout exchange every integration event is published to; other services bind their own queue.</summary>
    public const string EventsExchange = "relay.events";

    /// <summary>Durable local queue that runs the API's own event handlers (e.g. Audit).</summary>
    public const string LocalEventsQueue = "relay-events";

    public const string RabbitConnectionName = "rabbitmq";

    /// <summary>
    /// Wolverine setup for the API and MigrationService (ADR 0006): durable envelope storage in
    /// PostgreSQL, EF Core outbox integration, durable local queues and inbox for in-process handlers,
    /// and, when RabbitMQ is configured, every integration event also goes to <see cref="EventsExchange"/>.
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
        var rabbit = builder.Configuration.GetConnectionString(RabbitConnectionName);

        builder.UseWolverine(opts =>
        {
            ApplyCommon(opts, builder, handlerAssemblies);
            opts.PersistMessagesWithPostgresql(cs, WolverineSchema);
            opts.UseEntityFrameworkCoreTransactions();
            opts.Policies.UseDurableLocalQueues();
            opts.Policies.UseDurableInboxOnAllListeners();
            opts.AutoBuildMessageStorageOnStartup = buildStorage ? AutoCreate.CreateOrUpdate : AutoCreate.None;

            if (!string.IsNullOrEmpty(rabbit)) opts.UseRabbitMq(new Uri(rabbit)).AutoProvision();

            // An explicit publishing rule switches off Wolverine's automatic local routing, so the rule
            // names both destinations: the local queue for in-process handlers and the shared exchange.
            opts.Publish(rule =>
            {
                rule.MessagesImplementing<IIntegrationEvent>();
                rule.ToLocalQueue(LocalEventsQueue);
                if (!string.IsNullOrEmpty(rabbit)) rule.ToRabbitExchange(EventsExchange);
            });
        });

        return builder;
    }

    /// <summary>
    /// For services that only react to events (e.g. Realtime): binds <paramref name="queue"/> to the
    /// events exchange and runs this host's handlers. No database: a missed push is recovered by
    /// clients re-fetching, so the queue doesn't need Wolverine's durable inbox.
    /// </summary>
    public static IHostApplicationBuilder AddRelayEventSubscriber(
        this IHostApplicationBuilder builder, string queue, params Assembly[] handlerAssemblies)
    {
        var rabbit = builder.Configuration.GetConnectionString(RabbitConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{RabbitConnectionName}' is missing.");

        builder.UseWolverine(opts =>
        {
            ApplyCommon(opts, builder, handlerAssemblies);
            opts.UseRabbitMq(new Uri(rabbit))
                .AutoProvision()
                .DeclareExchange(EventsExchange, exchange => exchange.BindQueue(queue));
            opts.ListenToRabbitQueue(queue);
        });

        return builder;
    }

    private static void ApplyCommon(WolverineOptions opts, IHostApplicationBuilder builder, Assembly[] handlerAssemblies)
    {
        opts.ServiceName = builder.Environment.ApplicationName;

        // Modules keep their implementations internal (ADR 0002), so Wolverine's generated
        // handler code can't construct them inline; let it resolve them from the container.
        opts.ServiceLocationPolicy = JasperFx.CodeGeneration.Model.ServiceLocationPolicy.AlwaysAllowed;

        // Transient failures are retried with backoff, then the message is dead-lettered
        // (into wolverine_dead_letters where storage exists), where it can be inspected and replayed.
        opts.OnException<Exception>()
            .RetryWithCooldown(TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5))
            .Then.MoveToErrorQueue();

        foreach (var assembly in handlerAssemblies)
            opts.Discovery.IncludeAssembly(assembly);
    }
}
