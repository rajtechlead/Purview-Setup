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
    Configuration,
    Unhealthy
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
/// Tries approved providers in priority order. Providers in a cooldown window are skipped, a
/// provider that reports itself unhealthy is not dispatched to, and quota, auth, rate-limit,
/// timeout and server failures move to the next provider. The attempt and its token/cost
/// footprint are recorded either way.
/// </summary>
public sealed class AiFailoverOrchestrator
{
    private readonly IAiProviderFactory _factory;
    private readonly IAiUsageStore _usage;
    private readonly IClock _clock;
    private readonly TimeSpan _providerCooldown;
    private readonly TimeSpan _healthCheckTimeout;
    private readonly Dictionary<string, DateTimeOffset> _cooldownUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cooldownGate = new();

    public AiFailoverOrchestrator(
        IAiProviderFactory factory,
        IAiUsageStore usage,
        IClock clock,
        TimeSpan? providerCooldown = null,
        TimeSpan? healthCheckTimeout = null)
    {
        _factory = factory;
        _usage = usage;
        _clock = clock;
        _providerCooldown = providerCooldown ?? TimeSpan.FromMinutes(2);
        _healthCheckTimeout = healthCheckTimeout ?? TimeSpan.FromSeconds(5);
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

            if (IsCoolingDown(descriptor.Id))
            {
                failures.Add($"{descriptor.DisplayName}: cooling down after a recent failure");
                continue;
            }

            IAiProvider provider;
            try
            {
                provider = _factory.Create(descriptor);
            }
            catch (Exception ex)
            {
                failures.Add($"{descriptor.DisplayName}: configuration ({ex.Message})");
                await RecordFailureAsync(descriptor, AiFailureKind.Configuration, cancellationToken).ConfigureAwait(false);
                StartCooldown(descriptor.Id);
                continue;
            }

            if (!await IsHealthyAsync(provider, cancellationToken).ConfigureAwait(false))
            {
                failures.Add($"{descriptor.DisplayName}: health check failed");
                await RecordFailureAsync(descriptor, AiFailureKind.Unhealthy, cancellationToken).ConfigureAwait(false);
                StartCooldown(descriptor.Id);
                continue;
            }

            try
            {
                var response = await provider.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
                ClearCooldown(descriptor.Id);

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
                StartCooldown(descriptor.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                failures.Add($"{descriptor.DisplayName}: timeout");
                await RecordFailureAsync(descriptor, AiFailureKind.Timeout, cancellationToken).ConfigureAwait(false);
                StartCooldown(descriptor.Id);
                _ = ex;
            }
            catch (Exception ex)
            {
                failures.Add($"{descriptor.DisplayName}: {ex.GetType().Name}");
                await RecordFailureAsync(descriptor, AiFailureKind.ServerError, cancellationToken).ConfigureAwait(false);
                StartCooldown(descriptor.Id);
                _ = ex;
            }
        }

        throw new InvalidOperationException($"All AI providers failed. {string.Join(" | ", failures)}");
    }

    private async Task<bool> IsHealthyAsync(IAiProvider provider, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_healthCheckTimeout);

        try
        {
            return await provider.IsHealthyAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool IsCoolingDown(string providerId)
    {
        lock (_cooldownGate)
        {
            return _cooldownUntil.TryGetValue(providerId, out var until) && until > _clock.UtcNow;
        }
    }

    private void StartCooldown(string providerId)
    {
        lock (_cooldownGate)
        {
            _cooldownUntil[providerId] = _clock.UtcNow + _providerCooldown;
        }
    }

    private void ClearCooldown(string providerId)
    {
        lock (_cooldownGate)
        {
            _cooldownUntil.Remove(providerId);
        }
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
