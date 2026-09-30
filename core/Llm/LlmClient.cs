using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Diagnyx.Core.Llm;

/// <summary>
/// Minimal client for any OpenAI-compatible chat completions endpoint --
/// OpenAI itself, and the many other providers and self-hosted/local
/// runtimes (Ollama, LM Studio, ...) that speak the same request/response
/// shape. No vendor-specific SDK; multi-provider support beyond this shape
/// is a separate, later concern.
/// </summary>
internal static class LlmClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>Sends one chat completion request and returns the assistant's answer and citations.</summary>
    /// <exception cref="InvalidOperationException">
    /// The request failed, or the response wasn't a recognizable chat completion.
    /// </exception>
    public static AskResult Ask(string baseUrl, string model, string apiKey, string userMessage)
    {
        var url = BuildUrl(baseUrl);
        var body = BuildRequestBody(model, userMessage);

        using var client = new HttpClient { Timeout = Timeout };
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = Send(client, request);
        var responseText = new StreamReader(response.Content.ReadAsStream()).ReadToEnd();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"{(int)response.StatusCode} {response.ReasonPhrase}" +
                (string.IsNullOrWhiteSpace(responseText) ? "" : $" -- {responseText}"));

        return ExtractResult(responseText);
    }

    private static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request)
    {
        try
        {
            return client.Send(request);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private static string BuildUrl(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + "/chat/completions";
    }

    private static string BuildRequestBody(string model, string userMessage)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);

            writer.WritePropertyName("messages");
            writer.WriteStartArray();

            writer.WriteStartObject();
            writer.WriteString("role", "system");
            writer.WriteString("content", PromptBuilder.SystemMessage);
            writer.WriteEndObject();

            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", userMessage);
            writer.WriteEndObject();

            writer.WriteEndArray();

            // Asks the endpoint to return valid JSON for the {"answer", "citedEntries"}
            // shape the system message describes. Widely supported among
            // OpenAI-compatible endpoints, but not universal -- ExtractResult
            // falls back gracefully if a given endpoint ignores it.
            writer.WritePropertyName("response_format");
            writer.WriteStartObject();
            writer.WriteString("type", "json_object");
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static AskResult ExtractResult(string responseJson)
    {
        string content;
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
                throw new InvalidOperationException("the response contained no choices.");

            content = choices[0].GetProperty("message").GetProperty("content").GetString()
                ?? throw new InvalidOperationException("the response content was empty.");
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("the response content was empty.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
        {
            throw new InvalidOperationException($"unexpected response shape: {ex.Message}", ex);
        }

        return ParseStructuredContent(content);
    }

    /// <summary>
    /// Parses the model's reply as {"answer": string, "citedEntries": [int, ...]}.
    /// If the endpoint didn't honor response_format and returned plain prose
    /// (or any other shape) instead, falls back to treating the whole reply
    /// as the answer with no citations -- AskCommand then labels it
    /// unsupported, which is the honest outcome when we can't verify what,
    /// if anything, backed the answer.
    /// </summary>
    private static AskResult ParseStructuredContent(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new AskResult(content, Array.Empty<int>());

            if (!root.TryGetProperty("answer", out var answerElement) ||
                answerElement.ValueKind != JsonValueKind.String)
                return new AskResult(content, Array.Empty<int>());

            var answer = answerElement.GetString() ?? content;

            var citedEntries = new List<int>();
            if (root.TryGetProperty("citedEntries", out var citedElement) &&
                citedElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in citedElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number))
                        citedEntries.Add(number);
                }
            }

            return new AskResult(answer, citedEntries);
        }
        catch (JsonException)
        {
            return new AskResult(content, Array.Empty<int>());
        }
    }
}
