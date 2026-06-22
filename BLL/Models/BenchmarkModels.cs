namespace BLL.Models;

public sealed record RetrievalBenchmarkResult(
    string Category,
    string Name,
    string Status,
    double PrecisionAt3,
    double RecallAt3,
    double MeanReciprocalRank,
    double AverageLatencyMs,
    int EvaluationCount,
    string Note);

public sealed record GenerationBenchmarkResult(
    string Approach,
    string Status,
    double DocumentCoverage,
    double AverageLatencyMs,
    int EvaluationCount,
    string Note);

public sealed record BenchmarkDashboard(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<RetrievalBenchmarkResult> ChunkingResults,
    IReadOnlyList<RetrievalBenchmarkResult> EmbeddingResults,
    IReadOnlyList<GenerationBenchmarkResult> GenerationResults);

public sealed record OriginalDocumentFile(byte[] Content, string ContentType, string FileName);
