namespace ass2.Models;

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
    DateTimeOffset UploadedAt);

public sealed record SourceMatch(
    Guid DocumentId,
    string Title,
    string Subject,
    string Chapter,
    string Teacher,
    string Snippet,
    int Score);

public sealed record ChatTurn(
    string SessionId,
    string Question,
    string Answer,
    IReadOnlyList<SourceMatch> Sources,
    DateTimeOffset CreatedAt);
