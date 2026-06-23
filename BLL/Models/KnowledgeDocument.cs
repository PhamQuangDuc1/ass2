namespace BLL.Models;

public sealed record KnowledgeDocument(
    Guid Id,
    string Title,
    string FileName,
    string Department,
    string Subject,
    string Chapter,
    string Teacher,
    string UploadedBy,
    string Content,
    DateTimeOffset UploadedAt,
    bool HasOriginalFile);

public sealed record KnowledgeDocumentChunk(
    Guid DocumentId,
    int ChunkIndex,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record SourceMatch(
    Guid DocumentId,
    string Title,
    string Subject,
    string Chapter,
    string Teacher,
    string Snippet,
    int Score,
    string FileName,
    bool HasOriginalFile);

public sealed record ChatTurn(
    string SessionId,
    string Question,
    string Answer,
    IReadOnlyList<SourceMatch> Sources,
    DateTimeOffset CreatedAt);
