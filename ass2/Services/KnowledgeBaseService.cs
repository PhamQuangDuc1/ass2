using System.Text;
using System.Text.RegularExpressions;
using ass2.Data;
using ass2.Models;
using Microsoft.EntityFrameworkCore;

namespace ass2.Services;

public sealed class KnowledgeBaseService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    GeminiAiService geminiAi)
{
    public async Task<IReadOnlyList<KnowledgeDocument>> GetDocumentsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Documents
            .AsNoTracking()
            .OrderByDescending(document => document.UploadedAt)
            .Select(document => ToModel(document))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatTurn>> GetHistoryAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ChatTurns
            .AsNoTracking()
            .Include(turn => turn.Sources)
            .Where(turn => turn.SessionId == sessionId)
            .OrderBy(turn => turn.CreatedAt)
            .Select(turn => ToModel(turn))
            .ToListAsync(cancellationToken);
    }

    public async Task<KnowledgeDocument> AddDocumentAsync(
        IFormFile? file,
        string title,
        string department,
        string subject,
        string chapter,
        string teacher,
        string uploadedBy,
        string manualContent,
        CancellationToken cancellationToken)
    {
        var fileName = file?.FileName ?? "manual-note.txt";
        var content = string.IsNullOrWhiteSpace(manualContent)
            ? await ExtractReadableContentAsync(file, cancellationToken)
            : manualContent;

        if (string.IsNullOrWhiteSpace(content))
        {
            content = $"Tai lieu {title} ({fileName}) thuoc mon {subject}, {chapter}, giang vien {teacher}.";
        }

        var document = new KnowledgeDocument(
            Guid.NewGuid(),
            title.Trim(),
            fileName,
            department.Trim(),
            subject.Trim(),
            chapter.Trim(),
            teacher.Trim(),
            uploadedBy.Trim(),
            content.Trim(),
            DateTimeOffset.Now);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.Documents.Add(ToEntity(document));
        await db.SaveChangesAsync(cancellationToken);

        return document;
    }

    public async Task<ChatTurn> AskAsync(string sessionId, string question, CancellationToken cancellationToken = default)
    {
        var matches = await SearchAsync(question, 3, cancellationToken);
        var answer = await geminiAi.GenerateAnswerAsync(question, matches, cancellationToken)
            ?? BuildAnswer(question, matches);
        var turn = new ChatTurn(sessionId, question.Trim(), answer, matches, DateTimeOffset.Now);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.ChatTurns.Add(ToEntity(turn));
        await db.SaveChangesAsync(cancellationToken);

        return turn;
    }

    public async Task<IReadOnlyDictionary<string, int>> SubjectCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var counts = await db.Documents
            .AsNoTracking()
            .GroupBy(document => document.Subject)
            .Select(group => new
            {
                Subject = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        return counts
            .OrderBy(item => item.Subject)
            .ToDictionary(item => item.Subject, item => item.Count, StringComparer.OrdinalIgnoreCase);
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        if (await db.Documents.AnyAsync(cancellationToken))
        {
            return;
        }

        db.Documents.AddRange(
            ToEntity(CreateSeedDocument("PRN222 Razor Pages", "PRN222", "Razor Pages", "Chapter 1", "Nguyen Van A",
                "Razor Pages gom giao dien cshtml va code-behind cshtml.cs. Mo hinh nay phu hop demo nhanh web ASP.NET Core.",
                "System", 1)),
            ToEntity(CreateSeedDocument("SignalR realtime", "PRN222", "SignalR", "Chapter 2", "Tran Thi B",
                "SignalR cho phep server day thong bao realtime den trinh duyet. Hub nhan cau hoi va gui cau tra loi ve cac client trong cung phien.",
                "System", 2)),
            ToEntity(CreateSeedDocument("RAG benchmark", "AI", "RAG", "Chapter 3", "Le Van C",
                "RAG truy xuat tai lieu lien quan truoc khi tra loi. Benchmark co the so sanh multilingual-e5, text-embedding-3-small, PhoBERT va bge-m3 bang precision, recall va faithfulness.",
                "System", 3)));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static KnowledgeDocument CreateSeedDocument(string title, string department, string subject, string chapter, string teacher, string content, string uploadedBy, int index)
    {
        return new KnowledgeDocument(
            Guid.NewGuid(),
            title,
            $"{title}.txt",
            department,
            subject,
            chapter,
            teacher,
            uploadedBy,
            content,
            DateTimeOffset.Now.AddMinutes(-index));
    }

    private async Task<IReadOnlyList<SourceMatch>> SearchAsync(string question, int take, CancellationToken cancellationToken)
    {
        var tokens = Tokenize(question);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var documents = await db.Documents
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return documents
            .Select(document =>
            {
                var searchableText = $"{document.Title} {document.Subject} {document.Chapter} {document.Teacher} {document.Content}";
                var searchableTokens = Tokenize(searchableText);
                var score = tokens.Count(token => searchableTokens.Contains(token, StringComparer.OrdinalIgnoreCase));

                return new SourceMatch(
                    document.Id,
                    document.Title,
                    document.Subject,
                    document.Chapter,
                    document.Teacher,
                    MakeSnippet(document.Content, tokens),
                    score);
            })
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Title)
            .Take(take)
            .ToList();
    }

    private static string BuildAnswer(string question, IReadOnlyList<SourceMatch> matches)
    {
        if (matches.Count == 0)
        {
            return "Chua tim thay noi dung phu hop trong kho tai lieu. Hay upload them tai lieu hoac dat cau hoi cu the hon.";
        }

        var builder = new StringBuilder();
        builder.Append("Dua tren tai lieu da upload: ");
        builder.Append(matches[0].Snippet);

        if (matches.Count > 1)
        {
            builder.Append(" Ngoai ra co the doi chieu them voi ");
            builder.Append(string.Join(", ", matches.Skip(1).Select(match => match.Title)));
            builder.Append('.');
        }

        builder.Append(" Cau hoi cua ban: ");
        builder.Append(question.Trim());

        return builder.ToString();
    }

    private static async Task<string> ExtractReadableContentAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not ".txt" and not ".md" and not ".csv")
        {
            return $"Da nhan file {file.FileName}. Vui long nhap tom tat noi dung vao o noi dung de chatbot co ngu canh chinh xac.";
        }

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static HashSet<string> Tokenize(string value)
    {
        return Regex.Matches(value.ToLowerInvariant(), @"[\p{L}\p{Nd}]+")
            .Select(match => match.Value)
            .Where(token => token.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string MakeSnippet(string content, HashSet<string> tokens)
    {
        var sentences = Regex.Split(content, @"(?<=[.!?])\s+")
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
            .ToList();

        var bestSentence = sentences
            .OrderByDescending(sentence => Tokenize(sentence).Count(token => tokens.Contains(token)))
            .FirstOrDefault() ?? content;

        return bestSentence.Length <= 220 ? bestSentence : bestSentence[..220] + "...";
    }

    private static KnowledgeDocument ToModel(DocumentEntity document)
    {
        return new KnowledgeDocument(
            document.Id,
            document.Title,
            document.FileName,
            document.Department,
            document.Subject,
            document.Chapter,
            document.Teacher,
            document.UploadedBy,
            document.Content,
            document.UploadedAt);
    }

    private static ChatTurn ToModel(ChatTurnEntity turn)
    {
        return new ChatTurn(
            turn.SessionId,
            turn.Question,
            turn.Answer,
            turn.Sources
                .OrderByDescending(source => source.Score)
                .Select(source => new SourceMatch(
                    source.DocumentId,
                    source.Title,
                    source.Subject,
                    source.Chapter,
                    source.Teacher,
                    source.Snippet,
                    source.Score))
                .ToList(),
            turn.CreatedAt);
    }

    private static DocumentEntity ToEntity(KnowledgeDocument document)
    {
        return new DocumentEntity
        {
            Id = document.Id,
            Title = document.Title,
            FileName = document.FileName,
            Department = document.Department,
            Subject = document.Subject,
            Chapter = document.Chapter,
            Teacher = document.Teacher,
            UploadedBy = document.UploadedBy,
            Content = document.Content,
            UploadedAt = document.UploadedAt
        };
    }

    private static ChatTurnEntity ToEntity(ChatTurn turn)
    {
        var id = Guid.NewGuid();
        return new ChatTurnEntity
        {
            Id = id,
            SessionId = turn.SessionId,
            Question = turn.Question,
            Answer = turn.Answer,
            CreatedAt = turn.CreatedAt,
            Sources = turn.Sources.Select(source => new SourceMatchEntity
            {
                Id = Guid.NewGuid(),
                ChatTurnId = id,
                DocumentId = source.DocumentId,
                Title = source.Title,
                Subject = source.Subject,
                Chapter = source.Chapter,
                Teacher = source.Teacher,
                Snippet = source.Snippet,
                Score = source.Score
            }).ToList()
        };
    }
}
