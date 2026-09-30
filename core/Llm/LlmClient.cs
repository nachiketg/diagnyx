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

    /// <summary>Sends one chat completion request and returns the assistant's reply text.</summary>
    /// <exception cref="InvalidOperationException">
    /// The request failed, or the response wasn't a recognizable chat completion.
    /// </exception>
    public static string Ask(string baseUrl, string model, string apiKey, string userMessage)
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

        return ExtractAnswer(responseText);
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
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string ExtractAnswer(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
                throw new InvalidOperationException("the response contained no choices.");

            var content = choices[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("the response content was empty.");

            return content;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
        {
            throw new InvalidOperationException($"unexpected response shape: {ex.Message}", ex);
        }
    }
}
