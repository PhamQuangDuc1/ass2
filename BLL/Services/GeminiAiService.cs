using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using BLL.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

public sealed class GeminiAiService(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiAiService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string?> GenerateAnswerAsync(
        string question,
        IReadOnlyList<SourceMatch> sources,
        IReadOnlyList<ChatTurn> history,
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

        var prompt = BuildPrompt(question, sources, history);
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

    private static string BuildPrompt(string question, IReadOnlyList<SourceMatch> sources, IReadOnlyList<ChatTurn> history)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Ban la chatbot hoi dap tai lieu cho sinh vien.");
        builder.AppendLine("Tra loi tu nhien theo ngu canh hoi thoai gan day va ngu canh tai lieu ben duoi.");
        builder.AppendLine("Neu cau hoi la cau noi tiep nhu 'cai do', 'phan nay', 'noi ro hon', hay suy ra doi tuong tu lich su hoi thoai.");
        builder.AppendLine("Khong bia them ngoai tai lieu; neu khong du ngu canh, hay noi ro la chua co tai lieu phu hop.");
        builder.AppendLine("Tra loi bang tieng Viet, ngan gon, co nhac nguon tai lieu.");
        builder.AppendLine();

        builder.AppendLine("Lich su hoi thoai gan day:");
        if (history.Count == 0)
        {
            builder.AppendLine("- Chua co hoi thoai truoc do.");
        }
        else
        {
            foreach (var turn in history.TakeLast(6))
            {
                builder.AppendLine($"- Sinh vien: {turn.Question}");
                builder.AppendLine($"  Bot: {Limit(turn.Answer, 700)}");
            }
        }

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

    private static string Limit(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private sealed record GeminiRequest(IReadOnlyList<GeminiContent> Contents);
    private sealed record GeminiContent(IReadOnlyList<GeminiPart> Parts);
    private sealed record GeminiPart(string Text);
    private sealed record GeminiResponse(IReadOnlyList<GeminiCandidate>? Candidates);
    private sealed record GeminiCandidate(GeminiContent? Content);
}
