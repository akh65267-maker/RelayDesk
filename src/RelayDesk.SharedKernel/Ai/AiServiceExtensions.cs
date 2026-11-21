using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OllamaSharp;

namespace RelayDesk.SharedKernel.Ai;

/// <summary>Model settings (ADR 0008). Defaults target a local Ollama; swap the client registration for a cloud provider.</summary>
public sealed class AiOptions
{
    public const string Section = "Ai";

    public string Endpoint { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "llama3.2:3b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Must match the embedding model and the vector column size in the knowledge base.</summary>
    public int EmbeddingDimensions { get; set; } = 768;

    /// <summary>Tokens per workspace per calendar month before AI features pause (until plans arrive in Phase 5).</summary>
    public long MonthlyTokenQuota { get; set; } = 500_000;

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(2);
}

public static class AiServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IChatClient"/> and <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> through
    /// Microsoft.Extensions.AI, so features depend on abstractions, not on a provider.
    /// </summary>
    public static IHostApplicationBuilder AddRelayAi(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(AiOptions.Section).Get<AiOptions>() ?? new AiOptions();
        builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.Section));

        var endpoint = new Uri(options.Endpoint);
        builder.Services
            .AddChatClient(_ => new OllamaApiClient(new HttpClient { BaseAddress = endpoint, Timeout = options.RequestTimeout }, options.ChatModel))
            .UseOpenTelemetry();
        builder.Services
            .AddEmbeddingGenerator(_ => new OllamaApiClient(new HttpClient { BaseAddress = endpoint, Timeout = options.RequestTimeout }, options.EmbeddingModel))
            .UseOpenTelemetry();

        return builder;
    }
}
