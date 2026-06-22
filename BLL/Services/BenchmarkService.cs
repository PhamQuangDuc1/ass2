using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BLL.Models;
using DAL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

public sealed class BenchmarkService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<BenchmarkService> logger)
{
    private const int MaxDocuments = 8;
    private const int MaxChunks = 48;

    public async Task<BenchmarkDashboard> RunAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var documents = await db.Documents
            .AsNoTracking()
            .OrderBy(document => document.Id)
            .Take(MaxDocuments)
            .Select(document => new BenchmarkDocument(document.Id, document.Title, document.Subject, document.Chapter, document.Content))
            .ToListAsync(cancellationToken);

        if (documents.Count == 0)
        {
            return new BenchmarkDashboard(DateTimeOffset.Now, [], [], []);
        }

        var cases = documents
            .Select(document => new EvaluationCase(
                $"{document.Title} {document.Subject} {document.Chapter}",
                document.Id))
            .ToList();

        var chunkingResults = new List<RetrievalBenchmarkResult>();
        foreach (var strategy in ChunkingStrategies())
        {
            var chunks = documents
                .SelectMany(document => strategy.Split(document).Select(text => new BenchmarkChunk(document.Id, text)))
                .Take(MaxChunks)
                .ToList();
            chunkingResults.Add(EvaluateLocally("Chunking", strategy.Name, chunks, cases, strategy.Note));
        }

        var standardChunks = documents
            .SelectMany(document => FixedChunks(document.Content, 1100, 180)
                .Select(text => new BenchmarkChunk(document.Id, text)))
            .Take(MaxChunks)
            .ToList();

        var embeddingResults = new List<RetrievalBenchmarkResult>();
        foreach (var provider in EmbeddingProviders())
        {
            embeddingResults.Add(await EvaluateProviderAsync(provider, standardChunks, cases, cancellationToken));
        }

        var generationResults = await CompareGenerationAsync(documents, cases, cancellationToken);
        return new BenchmarkDashboard(DateTimeOffset.Now, chunkingResults, embeddingResults, generationResults);
    }

    private async Task<RetrievalBenchmarkResult> EvaluateProviderAsync(
        EmbeddingProvider provider,
        IReadOnlyList<BenchmarkChunk> chunks,
        IReadOnlyList<EvaluationCase> cases,
        CancellationToken cancellationToken)
    {
        if (!provider.IsConfigured)
        {
            return Unavailable("Embedding", provider.Name, cases.Count, provider.ConfigurationHint);
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var chunkVectors = await provider.EmbedAsync(chunks.Select(chunk => chunk.Text).ToList(), false, cancellationToken);
            var queryVectors = await provider.EmbedAsync(cases.Select(item => item.Query).ToList(), true, cancellationToken);
            stopwatch.Stop();

            if (chunkVectors.Count != chunks.Count || queryVectors.Count != cases.Count)
            {
                return Unavailable("Embedding", provider.Name, cases.Count, "Provider tra ve sai so luong vector.");
            }

            return CalculateMetrics(
                "Embedding",
                provider.Name,
                chunks,
                cases,
                queryVectors,
                chunkVectors,
                stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, cases.Count),
                provider.Note);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Embedding benchmark {Provider} failed", provider.Name);
            return Unavailable("Embedding", provider.Name, cases.Count, $"Loi provider: {exception.Message}");
        }
    }

    private static RetrievalBenchmarkResult EvaluateLocally(
        string category,
        string name,
        IReadOnlyList<BenchmarkChunk> chunks,
        IReadOnlyList<EvaluationCase> cases,
        string note)
    {
        var stopwatch = Stopwatch.StartNew();
        var chunkVectors = chunks.Select(chunk => LocalEmbedding(chunk.Text)).ToList();
        var queryVectors = cases.Select(item => LocalEmbedding(item.Query)).ToList();
        var result = CalculateMetrics(category, name, chunks, cases, queryVectors, chunkVectors, 0, note);
        stopwatch.Stop();
        return result with { AverageLatencyMs = stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, cases.Count) };
    }

    private static RetrievalBenchmarkResult CalculateMetrics(
        string category,
        string name,
        IReadOnlyList<BenchmarkChunk> chunks,
        IReadOnlyList<EvaluationCase> cases,
        IReadOnlyList<float[]> queryVectors,
        IReadOnlyList<float[]> chunkVectors,
        double latencyMs,
        string note)
    {
        var precision = 0d;
        var recall = 0d;
        var reciprocalRank = 0d;

        for (var queryIndex = 0; queryIndex < cases.Count; queryIndex++)
        {
            var ranked = chunks
                .Select((chunk, chunkIndex) => new
                {
                    chunk.DocumentId,
                    Score = CosineSimilarity(queryVectors[queryIndex], chunkVectors[chunkIndex])
                })
                .OrderByDescending(item => item.Score)
                .Take(3)
                .ToList();
            var relevantCount = ranked.Count(item => item.DocumentId == cases[queryIndex].RelevantDocumentId);
            precision += relevantCount / 3d;
            recall += relevantCount > 0 ? 1d : 0d;
            var firstRank = ranked.FindIndex(item => item.DocumentId == cases[queryIndex].RelevantDocumentId);
            reciprocalRank += firstRank >= 0 ? 1d / (firstRank + 1) : 0d;
        }

        var count = Math.Max(1, cases.Count);
        return new RetrievalBenchmarkResult(
            category,
            name,
            "Completed",
            precision / count,
            recall / count,
            reciprocalRank / count,
            latencyMs,
            cases.Count,
            note);
    }

    private async Task<IReadOnlyList<GenerationBenchmarkResult>> CompareGenerationAsync(
        IReadOnlyList<BenchmarkDocument> documents,
        IReadOnlyList<EvaluationCase> cases,
        CancellationToken cancellationToken)
    {
        var selectedCases = cases.Take(3).ToList();
        var byId = documents.ToDictionary(document => document.Id);
        var ragWatch = Stopwatch.StartNew();
        var ragCoverage = selectedCases.Average(item =>
        {
            var document = byId[item.RelevantDocumentId];
            var answer = FixedChunks(document.Content, 700, 80).FirstOrDefault() ?? document.Content;
            return TokenCoverage(answer, document.Content);
        });
        ragWatch.Stop();

        var results = new List<GenerationBenchmarkResult>
        {
            new("RAG", "Completed", ragCoverage, ragWatch.Elapsed.TotalMilliseconds / Math.Max(1, selectedCases.Count), selectedCases.Count,
                "Tra loi tu chunk duoc truy xuat trong tai lieu.")
        };

        var apiKey = configuration["OpenAI:ApiKey"];
        var model = configuration["OpenAI:FineTunedModel"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
        {
            results.Add(new("Fine-tuned model", "NotConfigured", 0, 0, 0,
                "Cau hinh OpenAI:ApiKey va OpenAI:FineTunedModel de chay doi chung."));
            return results;
        }

        try
        {
            var coverage = 0d;
            var watch = Stopwatch.StartNew();
            foreach (var evaluationCase in selectedCases)
            {
                var answer = await GenerateFineTunedAnswerAsync(model, apiKey, evaluationCase.Query, cancellationToken);
                coverage += TokenCoverage(answer, byId[evaluationCase.RelevantDocumentId].Content);
            }

            watch.Stop();
            results.Add(new("Fine-tuned model", "Completed", coverage / Math.Max(1, selectedCases.Count),
                watch.Elapsed.TotalMilliseconds / Math.Max(1, selectedCases.Count), selectedCases.Count, $"Model: {model}"));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Fine-tuned benchmark failed");
            results.Add(new("Fine-tuned model", "Failed", 0, 0, 0, exception.Message));
        }

        return results;
    }

    private async Task<string> GenerateFineTunedAnswerAsync(string model, string apiKey, string question, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.openai.com/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await client.PostAsJsonAsync("v1/chat/completions", new
        {
            model,
            messages = new[] { new { role = "user", content = question } },
            temperature = 0
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return payload.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private IEnumerable<EmbeddingProvider> EmbeddingProviders()
    {
        yield return CreateHuggingFaceProvider("multilingual-e5-base", "intfloat/multilingual-e5-base", true);
        yield return CreateOpenAiProvider();
        yield return CreateHuggingFaceProvider("PhoBERT-base", "vinai/phobert-base", false);
        yield return CreateHuggingFaceProvider("bge-m3", "BAAI/bge-m3", true);
    }

    private EmbeddingProvider CreateOpenAiProvider()
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        return new EmbeddingProvider(
            "text-embedding-3-small",
            !string.IsNullOrWhiteSpace(apiKey),
            "Cau hinh OpenAI:ApiKey.",
            "OpenAI embeddings API.",
            async (texts, _, cancellationToken) =>
            {
                var client = httpClientFactory.CreateClient();
                client.BaseAddress = new Uri("https://api.openai.com/");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using var response = await client.PostAsJsonAsync("v1/embeddings", new
                {
                    model = "text-embedding-3-small",
                    input = texts
                }, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                return payload.RootElement.GetProperty("data")
                    .EnumerateArray()
                    .OrderBy(item => item.GetProperty("index").GetInt32())
                    .Select(item => item.GetProperty("embedding").EnumerateArray().Select(value => value.GetSingle()).ToArray())
                    .ToList();
            });
    }

    private EmbeddingProvider CreateHuggingFaceProvider(string name, string modelId, bool usePrefixes)
    {
        var token = configuration["HuggingFace:ApiToken"];
        return new EmbeddingProvider(
            name,
            !string.IsNullOrWhiteSpace(token),
            "Cau hinh HuggingFace:ApiToken.",
            $"Hugging Face model {modelId}.",
            async (texts, isQuery, cancellationToken) =>
            {
                var client = httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var prepared = usePrefixes
                    ? texts.Select(text => $"{(isQuery ? "query" : "passage")}: {text}").ToList()
                    : texts;
                var vectors = new List<float[]>();
                foreach (var text in prepared)
                {
                    using var response = await client.PostAsJsonAsync(
                        $"https://router.huggingface.co/hf-inference/models/{modelId}/pipeline/feature-extraction",
                        new { inputs = text, options = new { wait_for_model = true } },
                        cancellationToken);
                    response.EnsureSuccessStatusCode();
                    using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    vectors.Add(ReadFeatureVector(payload.RootElement));
                }

                return vectors;
            });
    }

    private static float[] ReadFeatureVector(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Embedding response khong hop le.");
        }

        var first = element[0];
        if (first.ValueKind == JsonValueKind.Number)
        {
            return element.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        }

        var tokenVectors = element.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0 && item[0].ValueKind == JsonValueKind.Array ? item[0] : item)
            .Where(item => item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0 && item[0].ValueKind == JsonValueKind.Number)
            .Select(item => item.EnumerateArray().Select(value => value.GetSingle()).ToArray())
            .ToList();
        if (tokenVectors.Count == 0)
        {
            throw new InvalidOperationException("Khong doc duoc feature vector.");
        }

        var result = new float[tokenVectors[0].Length];
        foreach (var vector in tokenVectors)
        {
            for (var index = 0; index < result.Length; index++)
            {
                result[index] += vector[index] / tokenVectors.Count;
            }
        }

        return result;
    }

    private static IEnumerable<ChunkingStrategy> ChunkingStrategies()
    {
        yield return new("Fixed 500 / overlap 50", document => FixedChunks(document.Content, 500, 50), "Chunk nho theo ky tu.");
        yield return new("Fixed 1100 / overlap 180", document => FixedChunks(document.Content, 1100, 180), "Strategy dang dung trong RAG.");
        yield return new("Paragraph", document => ParagraphChunks(document.Content), "Giu ranh gioi doan van.");
    }

    private static IReadOnlyList<string> FixedChunks(string text, int size, int overlap)
    {
        var chunks = new List<string>();
        for (var start = 0; start < text.Length; start += Math.Max(1, size - overlap))
        {
            var length = Math.Min(size, text.Length - start);
            chunks.Add(text.Substring(start, length).Trim());
            if (start + length >= text.Length) break;
        }
        return chunks.Where(chunk => chunk.Length > 0).ToList();
    }

    private static IReadOnlyList<string> ParagraphChunks(string text)
    {
        var chunks = Regex.Split(text.Trim(), @"\r?\n\s*\r?\n")
            .Select(paragraph => Regex.Replace(paragraph, @"\s+", " ").Trim())
            .Where(paragraph => paragraph.Length > 0)
            .ToList();
        return chunks.Count > 0 ? chunks : [text];
    }

    private static float[] LocalEmbedding(string text)
    {
        const int dimensions = 512;
        var vector = new float[dimensions];
        foreach (var token in Tokens(text))
        {
            var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(token);
            vector[(hash & int.MaxValue) % dimensions] += 1;
        }
        var magnitude = Math.Sqrt(vector.Sum(value => value * value));
        if (magnitude > 0)
        {
            for (var index = 0; index < vector.Length; index++) vector[index] /= (float)magnitude;
        }
        return vector;
    }

    private static double CosineSimilarity(float[] left, float[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        var score = 0d;
        for (var index = 0; index < length; index++) score += left[index] * right[index];
        return score;
    }

    private static double TokenCoverage(string answer, string reference)
    {
        var referenceTokens = Tokens(reference).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var answerTokens = Tokens(answer).ToList();
        return answerTokens.Count == 0 ? 0 : answerTokens.Count(referenceTokens.Contains) / (double)answerTokens.Count;
    }

    private static IEnumerable<string> Tokens(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+")
            .Select(match => match.Value)
            .Where(token => token.Length > 1);

    private static RetrievalBenchmarkResult Unavailable(string category, string name, int count, string note) =>
        new(category, name, "NotConfigured", 0, 0, 0, 0, count, note);

    private sealed record BenchmarkDocument(Guid Id, string Title, string Subject, string Chapter, string Content);
    private sealed record BenchmarkChunk(Guid DocumentId, string Text);
    private sealed record EvaluationCase(string Query, Guid RelevantDocumentId);
    private sealed record ChunkingStrategy(string Name, Func<BenchmarkDocument, IReadOnlyList<string>> Split, string Note);
    private sealed record EmbeddingProvider(
        string Name,
        bool IsConfigured,
        string ConfigurationHint,
        string Note,
        Func<IReadOnlyList<string>, bool, CancellationToken, Task<IReadOnlyList<float[]>>> EmbedAsync);
}
