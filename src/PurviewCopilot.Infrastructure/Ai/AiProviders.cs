using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Ai;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Infrastructure.Ai;

/// <summary>Resolves provider keys from environment variables only; keys are never persisted.</summary>
public interface ISecretResolver
{
    string? Resolve(string keyReference);
}

public sealed class EnvironmentSecretResolver : ISecretResolver
{
    public string? Resolve(string keyReference)
    {
        if (string.IsNullOrWhiteSpace(keyReference)) return null;
        return Environment.GetEnvironmentVariable(keyReference);
    }
}

public sealed class AiProviderFactory : IAiProviderFactory
{
    private readonly HttpClient _http;
    private readonly ISecretResolver _secrets;

    public AiProviderFactory(HttpClient http, ISecretResolver secrets)
    {
        _http = http;
        _secrets = secrets;
    }

    public IAiProvider Create(AiProviderDescriptor descriptor)
    {
        var apiKey = _secrets.Resolve(descriptor.ApiKeyRef);

        if (!descriptor.IsLocal && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Provider {descriptor.DisplayName} requires a key in the environment variable '{descriptor.ApiKeyRef}'.");
        }

        return descriptor.Kind switch
        {
            AiProviderKind.Anthropic => new AnthropicProvider(descriptor, _http, apiKey),
            AiProviderKind.Gemini => new GeminiProvider(descriptor, _http, apiKey),
            _ => new OpenAiCompatibleProvider(descriptor, _http, apiKey)
        };
    }
}

public abstract class AiProviderBase : IAiProvider
{
    protected readonly HttpClient Http;
    protected readonly AiProviderDescriptor Descriptor;
    protected readonly string? ApiKey;

    protected AiProviderBase(AiProviderDescriptor descriptor, HttpClient http, string? apiKey)
    {
        Descriptor = descriptor;
        Http = http;
        ApiKey = apiKey;
    }

    public string Id => Descriptor.Id;

    public abstract Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default);

    public virtual Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    protected async Task<JsonDocument> SendAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException(Id, AiFailureKind.Timeout, $"{Descriptor.DisplayName} timed out after {timeout.TotalSeconds:0}s.");
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException(Id, AiFailureKind.ServerError, $"{Descriptor.DisplayName} is unreachable: {ex.Message}", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new AiProviderException(Id, Classify(response.StatusCode, body),
                    $"{Descriptor.DisplayName} returned {(int)response.StatusCode}: {Truncate(body)}");
            }

            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                throw new AiProviderException(Id, AiFailureKind.ServerError, "Provider returned a non-JSON response.", ex);
            }
        }
    }

    private static AiFailureKind Classify(HttpStatusCode status, string body)
    {
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return AiFailureKind.Unauthorized;
        if (status is HttpStatusCode.PaymentRequired or HttpStatusCode.TooManyRequests)
        {
            return body.Contains("quota", StringComparison.OrdinalIgnoreCase) || status == HttpStatusCode.PaymentRequired
                ? AiFailureKind.QuotaExhausted
                : AiFailureKind.RateLimited;
        }

        if (status == HttpStatusCode.RequestTimeout) return AiFailureKind.Timeout;
        return (int)status >= 500 ? AiFailureKind.ServerError : AiFailureKind.Configuration;
    }

    protected static string Truncate(string value) => value.Length <= 300 ? value : value[..300] + "…";

    protected static int ReadInt(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!TryStep(ref current, segment)) return 0;
        }

        return current.ValueKind == JsonValueKind.Number && current.TryGetInt32(out var value) ? value : 0;
    }

    protected static string ReadString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!TryStep(ref current, segment)) return string.Empty;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() ?? string.Empty : string.Empty;
    }

    private static bool TryStep(ref JsonElement current, string segment)
    {
        if (current.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index))
        {
            if (index < 0 || index >= current.GetArrayLength()) return false;
            current = current[index];
            return true;
        }

        if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next)) return false;

        current = next;
        return true;
    }
}

/// <summary>OpenAI-compatible chat completions: OpenAI, Azure OpenAI, DeepSeek, Grok, OpenRouter, Ollama, LM Studio, Open WebUI.</summary>
public sealed class OpenAiCompatibleProvider : AiProviderBase
{
    public OpenAiCompatibleProvider(AiProviderDescriptor descriptor, HttpClient http, string? apiKey)
        : base(descriptor, http, apiKey)
    {
    }

    public override async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (Descriptor.IsLocal)
        {
            try
            {
                using var response = await Http.GetAsync($"{Descriptor.Endpoint.TrimEnd('/')}/models", cancellationToken).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }

        return !string.IsNullOrWhiteSpace(ApiKey);
    }

    public override async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        var endpoint = Descriptor.Endpoint.TrimEnd('/');
        var url = Descriptor.Kind == AiProviderKind.AzureOpenAi
            ? $"{endpoint}/openai/deployments/{Descriptor.Model}/chat/completions?api-version=2024-10-21"
            : $"{endpoint}/chat/completions";

        using var message = new HttpRequestMessage(HttpMethod.Post, url);

        if (Descriptor.Kind == AiProviderKind.AzureOpenAi)
        {
            message.Headers.Add("api-key", ApiKey);
        }
        else if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        }

        message.Content = JsonContent.Create(new
        {
            model = Descriptor.Model,
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            stream = false,
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            }
        });

        using var document = await SendAsync(message, TimeSpan.FromSeconds(90), cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var content = ReadString(root, "choices", "0", "message", "content");
        if (string.IsNullOrWhiteSpace(content))
        {
            // Fall back to reasoning-style providers that return the text elsewhere.
            content = ReadString(root, "choices", "0", "text");
        }

        return new AiResponse(
            content,
            Descriptor.Id,
            Descriptor.Model,
            ReadInt(root, "usage", "prompt_tokens"),
            ReadInt(root, "usage", "completion_tokens"),
            0);
    }
}

public sealed class AnthropicProvider : AiProviderBase
{
    public AnthropicProvider(AiProviderDescriptor descriptor, HttpClient http, string? apiKey)
        : base(descriptor, http, apiKey)
    {
    }

    public override Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(ApiKey));

    public override async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{Descriptor.Endpoint.TrimEnd('/')}/messages");
        message.Headers.Add("x-api-key", ApiKey);
        message.Headers.Add("anthropic-version", "2023-06-01");
        message.Content = JsonContent.Create(new
        {
            model = Descriptor.Model,
            max_tokens = request.MaxTokens,
            temperature = request.Temperature,
            system = request.SystemPrompt,
            messages = new object[] { new { role = "user", content = request.UserPrompt } }
        });

        using var document = await SendAsync(message, TimeSpan.FromSeconds(90), cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var content = ReadString(root, "content", "0", "text");

        return new AiResponse(
            content,
            Descriptor.Id,
            Descriptor.Model,
            ReadInt(root, "usage", "input_tokens"),
            ReadInt(root, "usage", "output_tokens"),
            0);
    }
}

public sealed class GeminiProvider : AiProviderBase
{
    public GeminiProvider(AiProviderDescriptor descriptor, HttpClient http, string? apiKey)
        : base(descriptor, http, apiKey)
    {
    }

    public override Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(ApiKey));

    public override async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        var endpoint = Descriptor.Endpoint.TrimEnd('/');
        var url = $"{endpoint}/models/{Descriptor.Model}:generateContent?key={Uri.EscapeDataString(ApiKey ?? string.Empty)}";

        using var message = new HttpRequestMessage(HttpMethod.Post, url);
        message.Content = JsonContent.Create(new
        {
            systemInstruction = new { parts = new[] { new { text = request.SystemPrompt } } },
            contents = new object[]
            {
                new { role = "user", parts = new[] { new { text = request.UserPrompt } } }
            },
            generationConfig = new { temperature = request.Temperature, maxOutputTokens = request.MaxTokens }
        });

        using var document = await SendAsync(message, TimeSpan.FromSeconds(90), cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var content = ReadString(root, "candidates", "0", "content", "parts", "0", "text");

        return new AiResponse(
            content,
            Descriptor.Id,
            Descriptor.Model,
            ReadInt(root, "usageMetadata", "promptTokenCount"),
            ReadInt(root, "usageMetadata", "candidatesTokenCount"),
            0);
    }
}
