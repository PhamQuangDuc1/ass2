using System.Text;
using System.Text.Json;
using ass2.Models;

namespace ass2.Services;

public sealed class GeminiAiService(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiAiService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string?> GenerateAnswerAsync(
        string question,
        IReadOnlyList<SourceMatch> sources,
        CancellationToken cancellationToken)
    {
        var apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var model = configuration["Gemini:Model"];
        if (string.IsNullOrWhiteSpace(model))
        {
            model = "gemini-flash-latest";
        }

        var prompt = BuildPrompt(question, sources);
        var request = new GeminiRequest(
        [
            new GeminiContent(
            [
                new GeminiPart(prompt)
            ])
        ]);

        using var response = await httpClient.PostAsJsonAsync(
            $"v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}",
            request,
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Gemini API failed with {StatusCode}: {Error}", response.StatusCode, error);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<GeminiResponse>(JsonOptions, cancellationToken);
        return payload?.Candidates?
            .SelectMany(candidate => candidate.Content?.Parts ?? [])
            .Select(part => part.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
    }

    private static string BuildPrompt(string question, IReadOnlyList<SourceMatch> sources)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Ban la chatbot hoi dap tai lieu cho sinh vien.");
        builder.AppendLine("Chi tra loi dua tren ngu canh tai lieu ben duoi. Neu khong du ngu canh, hay noi ro la chua co tai lieu phu hop.");
        builder.AppendLine("Tra loi bang tieng Viet, ngan gon, co nhac nguon tai lieu.");
        builder.AppendLine();
        builder.AppendLine("Ngu canh tai lieu:");

        if (sources.Count == 0)
        {
            builder.AppendLine("- Khong co nguon phu hop.");
        }
        else
        {
            foreach (var source in sources)
            {
                builder.AppendLine($"- {source.Title} | {source.Subject} | {source.Chapter} | GV: {source.Teacher}");
                builder.AppendLine($"  Noi dung: {source.Snippet}");
            }
        }

        builder.AppendLine();
        builder.AppendLine($"Cau hoi: {question}");
        return builder.ToString();
    }

    private sealed record GeminiRequest(IReadOnlyList<GeminiContent> Contents);
    private sealed record GeminiContent(IReadOnlyList<GeminiPart> Parts);
    private sealed record GeminiPart(string Text);
    private sealed record GeminiResponse(IReadOnlyList<GeminiCandidate>? Candidates);
    private sealed record GeminiCandidate(GeminiContent? Content);
}
