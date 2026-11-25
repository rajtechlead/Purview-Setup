using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Ai;

public enum AiFailureKind
{
    QuotaExhausted = 0,
    Unauthorized,
    RateLimited,
    Timeout,
    ServerError,
    Configuration
}

public sealed class AiProviderException : Exception
{
    public AiProviderException(string providerId, AiFailureKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        ProviderId = providerId;
        Kind = kind;
    }

    public string ProviderId { get; }
    public AiFailureKind Kind { get; }
}

/// <summary>
/// Tries approved providers in priority order. Quota, auth and rate-limit failures move to the
/// next provider; the attempt and its token/cost footprint are recorded either way.
/// </summary>
public sealed class AiFailoverOrchestrator
{
    private readonly IAiProviderFactory _factory;
    private readonly IAiUsageStore _usage;
    private readonly IClock _clock;

    public AiFailoverOrchestrator(IAiProviderFactory factory, IAiUsageStore usage, IClock clock)
    {
        _factory = factory;
        _usage = usage;
        _clock = clock;
    }

    public async Task<AiResponse> CompleteAsync(
        IReadOnlyList<AiProviderDescriptor> providers,
        AiRequest request,
        CancellationToken cancellationToken = default)
    {
        var ordered = providers
            .Where(p => p.Enabled)
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ordered.Count == 0)
        {
            throw new InvalidOperationException("No AI providers are enabled. Enable a provider in Settings or switch to local-only mode.");
        }

        var failures = new List<string>();

        foreach (var descriptor in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IAiProvider provider;
            try
            {
                provider = _factory.Create(descriptor);
            }
            catch (Exception ex)
            {
                failures.Add($"{descriptor.DisplayName}: configuration ({ex.Message})");
                await RecordFailureAsync(descriptor, AiFailureKind.Configuration, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                var response = await provider.CompleteAsync(request, cancellationToken).ConfigureAwait(false);

                var cost = CalculateCost(descriptor, response.PromptTokens, response.CompletionTokens);
                await _usage.RecordAsync(new AiUsageRecord(
                    Guid.NewGuid(), descriptor.Id, response.Model,
                    response.PromptTokens, response.CompletionTokens, cost,
                    true, null, _clock.UtcNow), cancellationToken).ConfigureAwait(false);

                return response with
                {
                    ProviderId = descriptor.Id,
                    EstimatedCostUsd = cost
                };
            }
            catch (AiProviderException ex)
            {
                failures.Add($"{descriptor.DisplayName}: {ex.Kind}");
                await RecordFailureAsync(descriptor, ex.Kind, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                failures.Add($"{descriptor.DisplayName}: timeout");
                await RecordFailureAsync(descriptor, AiFailureKind.Timeout, cancellationToken).ConfigureAwait(false);
                _ = ex;
            }
            catch (Exception ex)
            {
                failures.Add($"{descriptor.DisplayName}: {ex.GetType().Name}");
                await RecordFailureAsync(descriptor, AiFailureKind.ServerError, cancellationToken).ConfigureAwait(false);
               _ = ex;
            }
        }

        throw new InvalidOperationException($"All AI providers failed. {string.Join(" | ", failures)}");
    }

    public static double CalculateCost(AiProviderDescriptor provider, int promptTokens, int completionTokens) =>
        Math.Round(
            promptTokens / 1000.0 * provider.InputCostPer1KTokens +
            completionTokens / 1000.0 * provider.OutputCostPer1KTokens, 6);

    private Task RecordFailureAsync(AiProviderDescriptor provider, AiFailureKind kind, CancellationToken cancellationToken) =>
        _usage.RecordAsync(new AiUsageRecord(
            Guid.NewGuid(), provider.Id, provider.Model, 0, 0, 0,
            false, kind.ToString(), _clock.UtcNow), cancellationToken);
}
