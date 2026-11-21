using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using RelayDesk.SharedKernel.Ai;

namespace RelayDesk.AiWorker;

/// <summary>
/// Makes sure the local Ollama has the configured models (first start downloads them, a few GB).
/// Until then AI calls fail and their suggestions show as Failed; agents can retry, and the product works without AI.
/// </summary>
internal sealed class ModelBootstrapper(IOptions<AiOptions> options, ILogger<ModelBootstrapper> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = new Uri(options.Value.Endpoint), Timeout = TimeSpan.FromMinutes(30) };
        foreach (var model in new[] { options.Value.EmbeddingModel, options.Value.ChatModel })
        {
            for (var attempt = 1; !ct.IsCancellationRequested; attempt++)
            {
                try
                {
                    log.LogInformation("Ensuring Ollama model {Model} is available (downloads on first run)", model);
                    using var res = await http.PostAsJsonAsync("/api/pull", new { model, stream = false }, ct);
                    res.EnsureSuccessStatusCode();
                    log.LogInformation("Model {Model} ready", model);
                    break;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    log.LogWarning(ex, "Pulling {Model} failed (attempt {Attempt}); retrying", model, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * attempt)), ct);
                }
            }
        }
    }
}
