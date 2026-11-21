using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace RelayDesk.IntegrationTests.Infrastructure;

/// <summary>Deterministic stand-in for the language model: answers by prompt type and records every prompt.</summary>
internal sealed class FakeChatClient : IChatClient
{
    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        var list = messages.ToList();
        lock (Calls) Calls.Add(list);
        var system = list.First(m => m.Role == ChatRole.System).Text;
        var text = system.Contains("triage", StringComparison.OrdinalIgnoreCase)
            ? """{"category":"account","priority":"High","sentiment":"negative","reason":"Customer is locked out."}"""
            : system.Contains("Summarise")
                ? "- Customer can't log in\n- Reset link suggested"
                : "You can reset it from the sign-in page using \"Forgot password\" [1].";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = "fake-model",
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 },
        });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>
/// Bag-of-words embeddings: each word is hashed into one of 768 dimensions. Texts sharing words end up
/// close, which is enough to exercise retrieval without a real model.
/// </summary>
internal sealed class FakeEmbeddings : IEmbeddingGenerator<string, Embedding<float>>
{
    public const int Dimensions = 768;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken ct = default) =>
        Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(v => new Embedding<float>(Embed(v)))));

    private static float[] Embed(string text)
    {
        var v = new float[Dimensions];
        foreach (var word in text.ToLowerInvariant().Split([' ', '\n', '.', ',', '?', '!', '"'], StringSplitOptions.RemoveEmptyEntries))
            v[BitConverter.ToUInt16(SHA256.HashData(Encoding.UTF8.GetBytes(word))) % Dimensions] += 1;
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        return norm == 0 ? v.Select(_ => 1f / Dimensions).ToArray() : v.Select(x => x / norm).ToArray();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
