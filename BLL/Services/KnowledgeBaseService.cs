using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using BLL.Models;
using DAL.Data;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;
using DrawingText = DocumentFormat.OpenXml.Drawing.Text;
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;
using Microsoft.AspNetCore.Http;

namespace BLL.Services;

public sealed class KnowledgeBaseService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    GeminiAiService geminiAi)
{
    private const int EmbeddingDimensions = 256;
    private const int DefaultChunkSize = 1100;
    private const int DefaultChunkOverlap = 180;
    private const int ConversationContextTurns = 6;
    private const string ChunkSizeSettingKey = "ChunkSize";
    private const string ChunkOverlapSettingKey = "ChunkOverlap";
    private static readonly JsonSerializerOptions EmbeddingJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<KnowledgeDocument>> GetDocumentsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        return await db.Documents
            .AsNoTracking()
            .OrderByDescending(document => document.UploadedAt)
            .Select(document => ToModel(document))
            .ToListAsync(cancellationToken);
    }

    public async Task<KnowledgeDocument?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        var document = await db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == documentId, cancellationToken);

        return document is null ? null : ToModel(document);
    }

    public async Task<ChunkSettings> GetChunkSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);
        return await ReadChunkSettingsAsync(db, cancellationToken);
    }

    public async Task<ChunkSettings> UpdateChunkSettingsAsync(int chunkSize, int chunkOverlap, CancellationToken cancellationToken = default)
    {
        chunkSize = Math.Clamp(chunkSize, 200, 8000);
        chunkOverlap = Math.Clamp(chunkOverlap, 0, Math.Max(0, chunkSize - 1));

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);
        await UpsertSettingAsync(db, ChunkSizeSettingKey, chunkSize.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await UpsertSettingAsync(db, ChunkOverlapSettingKey, chunkOverlap.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new ChunkSettings(chunkSize, chunkOverlap);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<KnowledgeDocumentChunk>>> GetChunksByDocumentAsync(
        IEnumerable<Guid> documentIds,
        CancellationToken cancellationToken = default)
    {
        var ids = documentIds
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<KnowledgeDocumentChunk>>();
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        if (await db.Documents.AnyAsync(document => ids.Contains(document.Id) && !document.Chunks.Any(), cancellationToken))
        {
            await BackfillMissingChunksAsync(db, cancellationToken);
        }

        var chunks = await db.DocumentChunks
            .AsNoTracking()
            .Where(chunk => ids.Contains(chunk.DocumentId))
            .OrderBy(chunk => chunk.DocumentId)
            .ThenBy(chunk => chunk.ChunkIndex)
            .Select(chunk => new KnowledgeDocumentChunk(
                chunk.DocumentId,
                chunk.ChunkIndex,
                chunk.Content,
                chunk.CreatedAt))
            .ToListAsync(cancellationToken);

        return chunks
            .GroupBy(chunk => chunk.DocumentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<KnowledgeDocumentChunk>)group.ToList());
    }

    public async Task<IReadOnlyList<ChatTurn>> GetHistoryAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

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
        string uploadedByUserId,
        string manualContent,
        CancellationToken cancellationToken)
    {
        var fileName = file?.FileName ?? "manual-note.txt";
        var originalFile = await ReadOriginalFileAsync(file, cancellationToken);
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
            uploadedByUserId.Trim(),
            content.Trim(),
            DateTimeOffset.Now,
            originalFile.Length > 0);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        var documentEntity = ToEntity(document);
        documentEntity.ContentType = file?.ContentType ?? string.Empty;
        documentEntity.FileContent = originalFile.Length > 0 ? originalFile : null;
        documentEntity.Chunks = BuildChunkEntities(document, await ReadChunkSettingsAsync(db, cancellationToken)).ToList();
        db.Documents.Add(documentEntity);
        await db.SaveChangesAsync(cancellationToken);

        return document;
    }

    public async Task<KnowledgeDocument?> UpdateDocumentAsync(
        Guid documentId,
        IFormFile? file,
        string title,
        string department,
        string subject,
        string chapter,
        string teacher,
        string manualContent,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        var document = await db.Documents
            .FirstOrDefaultAsync(item => item.Id == documentId, cancellationToken);

        if (document is null)
        {
            return null;
        }

        var originalFile = await ReadOriginalFileAsync(file, cancellationToken);
        var content = string.IsNullOrWhiteSpace(manualContent)
            ? originalFile.Length > 0
                ? await ExtractReadableContentAsync(file, cancellationToken)
                : document.Content
            : manualContent;

        document.Title = string.IsNullOrWhiteSpace(title) ? document.Title : title.Trim();
        document.Department = department.Trim();
        document.Subject = subject.Trim();
        document.Chapter = chapter.Trim();
        document.Teacher = teacher.Trim();
        document.Content = string.IsNullOrWhiteSpace(content) ? document.Content : content.Trim();

        if (originalFile.Length > 0 && file is not null)
        {
            document.FileName = file.FileName;
            document.ContentType = file.ContentType ?? string.Empty;
            document.FileContent = originalFile;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.DocumentChunks
            .Where(chunk => chunk.DocumentId == document.Id)
            .ExecuteDeleteAsync(cancellationToken);
        db.DocumentChunks.AddRange(BuildChunkEntities(ToModel(document), await ReadChunkSettingsAsync(db, cancellationToken)));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToModel(document);
    }

    public async Task<bool> DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        var document = await db.Documents.FirstOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        if (document is null)
        {
            return false;
        }

        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<KnowledgeDocument?> ReindexDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        var document = await db.Documents
            .FirstOrDefaultAsync(item => item.Id == documentId, cancellationToken);

        if (document is null)
        {
            return null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.DocumentChunks
            .Where(chunk => chunk.DocumentId == document.Id)
            .ExecuteDeleteAsync(cancellationToken);
        db.DocumentChunks.AddRange(BuildChunkEntities(ToModel(document), await ReadChunkSettingsAsync(db, cancellationToken)));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToModel(document);
    }

    public async Task<OriginalDocumentFile?> GetOriginalFileAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        var document = await db.Documents
            .AsNoTracking()
            .Where(item => item.Id == documentId)
            .Select(item => new { item.FileContent, item.ContentType, item.FileName })
            .FirstOrDefaultAsync(cancellationToken);

        return document?.FileContent is { Length: > 0 }
            ? new OriginalDocumentFile(document.FileContent, document.ContentType, document.FileName)
            : null;
    }

    public async Task<ChatTurn> AskAsync(string sessionId, string question, CancellationToken cancellationToken = default)
    {
        var history = await GetRecentHistoryAsync(sessionId, ConversationContextTurns, cancellationToken);
        var retrievalQuery = BuildRetrievalQuery(question, history);
        var matches = await SearchAsync(retrievalQuery, question, 3, cancellationToken);
        var answer = matches.Count == 0
            ? BuildAnswer(question, matches, history)
            : await geminiAi.GenerateAnswerAsync(question, matches, history, cancellationToken)
                ?? BuildAnswer(question, matches, history);
        var turn = new ChatTurn(sessionId, question.Trim(), answer, matches, DateTimeOffset.Now);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        db.ChatTurns.Add(ToEntity(turn));
        await db.SaveChangesAsync(cancellationToken);

        return turn;
    }

    public async Task<IReadOnlyDictionary<string, int>> SubjectCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

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
        await EnsureSchemaAsync(db, cancellationToken);

        if (await db.Documents.AnyAsync(cancellationToken))
        {
            await BackfillMissingChunksAsync(db, cancellationToken);
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
        await BackfillMissingChunksAsync(db, cancellationToken);
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
            uploadedBy,
            content,
            DateTimeOffset.Now.AddMinutes(-index),
            false);
    }

    private async Task<IReadOnlyList<SourceMatch>> SearchAsync(string retrievalQuery, string question, int take, CancellationToken cancellationToken)
    {
        var queryEmbedding = EmbedText(retrievalQuery);
        var importantTokens = GetImportantTokens(question);
        if (importantTokens.Count == 0)
        {
            importantTokens = GetImportantTokens(retrievalQuery);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        if (!await db.DocumentChunks.AnyAsync(cancellationToken) && await db.Documents.AnyAsync(cancellationToken))
        {
            await BackfillMissingChunksAsync(db, cancellationToken);
        }

        var chunks = await db.DocumentChunks
            .AsNoTracking()
            .Include(chunk => chunk.Document)
            .ToListAsync(cancellationToken);

        return chunks
            .Select(chunk =>
            {
                var chunkTokens = Tokenize($"{chunk.Title} {chunk.Subject} {chunk.Chapter} {chunk.Content}");
                var tokenHits = importantTokens.Count(token => chunkTokens.Contains(token));
                var similarity = CosineSimilarity(queryEmbedding, DeserializeEmbedding(chunk.Embedding));
                var score = (int)Math.Round(similarity * 1000) + (tokenHits * 250);

                return new SourceMatch(
                    chunk.DocumentId,
                    chunk.Title,
                    chunk.Subject,
                    chunk.Chapter,
                    chunk.Teacher,
                    MakeSnippet(chunk.Content, question),
                    score,
                    chunk.Document?.FileName ?? string.Empty,
                    chunk.Document?.FileContent is { Length: > 0 });
            })
            .Where(match => importantTokens.Count == 0 || importantTokens.Any(token => Tokenize(match.Snippet).Contains(token)))
            .Where(match => match.Score >= 250)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Title)
            .Take(take)
            .ToList();
    }

    private async Task<IReadOnlyList<ChatTurn>> GetRecentHistoryAsync(string sessionId, int take, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSchemaAsync(db, cancellationToken);

        return await db.ChatTurns
            .AsNoTracking()
            .Include(turn => turn.Sources)
            .Where(turn => turn.SessionId == sessionId)
            .OrderByDescending(turn => turn.CreatedAt)
            .Take(take)
            .OrderBy(turn => turn.CreatedAt)
            .Select(turn => ToModel(turn))
            .ToListAsync(cancellationToken);
    }

    private static string BuildRetrievalQuery(string question, IReadOnlyList<ChatTurn> history)
    {
        if (history.Count == 0)
        {
            return question;
        }

        var builder = new StringBuilder();
        foreach (var turn in history.TakeLast(3))
        {
            builder.Append(turn.Question);
            builder.Append(' ');
            builder.Append(string.Join(' ', turn.Sources.Select(source => source.Snippet)));
            builder.Append(' ');
        }

        builder.Append(question);
        return builder.ToString();
    }

    private static string BuildAnswer(string question, IReadOnlyList<SourceMatch> matches, IReadOnlyList<ChatTurn> history)
    {
        if (matches.Count == 0)
        {
            return "Chua tim thay noi dung phu hop trong kho tai lieu. Hay upload them tai lieu hoac dat cau hoi cu the hon.";
        }

        var isFollowUp = IsFollowUpQuestion(question);
        var builder = new StringBuilder();
        builder.Append(isFollowUp && history.Count > 0
            ? "Y cua phan truoc la: "
            : "Theo tai lieu da index: ");
        builder.Append(CleanSnippet(matches[0].Snippet));

        if (matches.Count > 1)
        {
            var relatedTitles = matches
                .Skip(1)
                .Select(match => match.Title)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();

            if (relatedTitles.Count > 0)
            {
                builder.Append(" Co the doi chieu them trong ");
                builder.Append(string.Join(", ", relatedTitles));
            }

            builder.Append('.');
        }

        return builder.ToString();
    }

    private static bool IsFollowUpQuestion(string question)
    {
        var normalized = RemoveDiacritics(question.Trim().ToLowerInvariant());
        if (normalized.Contains("giai thich them", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("vi du them", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalized.Contains("noi ro hon", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("nói rõ hơn", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("phan tren", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("phần trên", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("cai do", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("cái đó", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("y tren", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ý trên", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanSnippet(string snippet)
    {
        var cleaned = Regex.Replace(snippet, @"\s+", " ").Trim();
        cleaned = Regex.Replace(cleaned, @"\b[A-Za-z0-9 _-]+\.(pdf|docx|pptx|txt|md|csv)\b", "", RegexOptions.IgnoreCase).Trim();
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
        return cleaned;
    }

    private static async Task<string> ExtractReadableContentAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        return extension switch
        {
            ".txt" or ".md" or ".csv" => await ExtractPlainTextAsync(file, cancellationToken),
            ".pdf" => ExtractPdfText(file),
            ".docx" => ExtractDocxText(file),
            ".pptx" => ExtractPptxText(file),
            ".ppt" => $"Da nhan file {file.FileName}. File PPT doi cu chua duoc doc truc tiep, vui long luu thanh PPTX hoac nhap tom tat noi dung.",
            _ => $"Da nhan file {file.FileName}. Dinh dang nay chua duoc ho tro doc noi dung truc tiep."
        };
    }

    private static async Task<byte[]> ReadOriginalFileAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return [];
        }

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    private static async Task<string> ExtractPlainTextAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static string ExtractPdfText(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var pdf = PdfDocument.Open(stream);

        var builder = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            var words = page.GetWords()
                .Select(word => word.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            if (words.Count > 0)
            {
                builder.AppendLine(string.Join(' ', words));
            }
            else if (!string.IsNullOrWhiteSpace(page.Text))
            {
                builder.AppendLine(page.Text);
            }
        }

        return builder.ToString();
    }

    private static string ExtractDocxText(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document?.Body;

        if (body is null)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine,
            body
                .Descendants<WordText>()
                .Select(text => text.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string ExtractPptxText(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var presentation = PresentationDocument.Open(stream, false);

        var builder = new StringBuilder();
        var slideParts = presentation.PresentationPart?.SlideParts ?? [];
        var slideNumber = 1;

        foreach (var slidePart in slideParts)
        {
            if (slidePart.Slide is null)
            {
                slideNumber++;
                continue;
            }

            var slideText = slidePart
                .Slide
                .Descendants<DrawingText>()
                .Select(text => text.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            if (slideText.Count > 0)
            {
                builder.AppendLine($"Slide {slideNumber}:");
                builder.AppendLine(string.Join(Environment.NewLine, slideText));
                builder.AppendLine();
            }

            slideNumber++;
        }

        return builder.ToString();
    }

    private static HashSet<string> Tokenize(string value)
    {
        return Regex.Matches(value.ToLowerInvariant(), @"[\p{L}\p{Nd}]+")
            .Select(match => RemoveDiacritics(match.Value))
            .Where(token => token.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> GetImportantTokens(string value)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ai", "anh", "ban", "bang", "bai", "bao", "bi", "bo", "cai", "cac", "can", "chapter",
            "cho", "chuong", "co", "cua", "da", "dang", "de", "den", "duoc", "gi", "hon", "la",
            "lam", "noi", "phan", "sao", "tai", "the", "thi", "trong", "tren", "voi", "ve", "va",
            "what", "who", "how", "the", "and", "or", "in", "on", "of", "to", "is", "are"
        };

        return Tokenize(value)
            .Where(token => token.Length > 2)
            .Where(token => !stopWords.Contains(token))
            .Where(token => !int.TryParse(token, out _))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var character in normalized)
        {
            if (character == '\u0111')
            {
                builder.Append('d');
                continue;
            }

            if (character == '\u0110')
            {
                builder.Append('D');
                continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace('đ', 'd')
            .Replace('Đ', 'D');
    }

    private static IEnumerable<DocumentChunkEntity> BuildChunkEntities(KnowledgeDocument document, ChunkSettings settings)
    {
        var chunks = ChunkText(document.Content, settings).ToList();
        if (chunks.Count == 0)
        {
            chunks.Add(document.Content);
        }

        return chunks.Select((chunk, index) => new DocumentChunkEntity
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            ChunkIndex = index,
            Title = document.Title,
            Department = document.Department,
            Subject = document.Subject,
            Chapter = document.Chapter,
            Teacher = document.Teacher,
            Content = chunk,
            Embedding = SerializeEmbedding(EmbedText($"{document.Title} {document.Department} {document.Subject} {document.Chapter} {document.Teacher} {chunk}")),
            CreatedAt = DateTimeOffset.Now
        });
    }

    private static async Task BackfillMissingChunksAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var documents = await db.Documents
            .Include(document => document.Chunks)
            .Where(document => !document.Chunks.Any())
            .ToListAsync(cancellationToken);
        var settings = await ReadChunkSettingsAsync(db, cancellationToken);

        foreach (var document in documents)
        {
            db.DocumentChunks.AddRange(BuildChunkEntities(ToModel(document), settings));
        }

        if (documents.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task EnsureSchemaAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            IF COL_LENGTH(N'[dbo].[Documents]', N'ContentType') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [ContentType] nvarchar(120) NOT NULL CONSTRAINT [DF_Documents_ContentType] DEFAULT N'';

            IF COL_LENGTH(N'[dbo].[Documents]', N'FileContent') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [FileContent] varbinary(max) NULL;

            IF COL_LENGTH(N'[dbo].[Documents]', N'UploadedByUserId') IS NULL
                ALTER TABLE [dbo].[Documents] ADD [UploadedByUserId] nvarchar(120) NOT NULL CONSTRAINT [DF_Documents_UploadedByUserId] DEFAULT N'';

            IF OBJECT_ID(N'[dbo].[DocumentChunks]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[DocumentChunks] (
                    [Id] uniqueidentifier NOT NULL,
                    [DocumentId] uniqueidentifier NOT NULL,
                    [ChunkIndex] int NOT NULL,
                    [Title] nvarchar(200) NOT NULL,
                    [Department] nvarchar(100) NOT NULL,
                    [Subject] nvarchar(120) NOT NULL,
                    [Chapter] nvarchar(120) NOT NULL,
                    [Teacher] nvarchar(120) NOT NULL,
                    [Content] nvarchar(max) NOT NULL,
                    [Embedding] nvarchar(max) NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_DocumentChunks] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_DocumentChunks_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents] ([Id]) ON DELETE CASCADE
                );

                CREATE INDEX [IX_DocumentChunks_DocumentId] ON [dbo].[DocumentChunks] ([DocumentId]);
            END

            IF OBJECT_ID(N'[dbo].[ChatTurns]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ChatTurns] (
                    [Id] uniqueidentifier NOT NULL,
                    [SessionId] nvarchar(80) NOT NULL,
                    [Question] nvarchar(max) NOT NULL,
                    [Answer] nvarchar(max) NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_ChatTurns] PRIMARY KEY ([Id])
                );
            END

            IF OBJECT_ID(N'[dbo].[SourceMatches]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[SourceMatches] (
                    [Id] uniqueidentifier NOT NULL,
                    [ChatTurnId] uniqueidentifier NOT NULL,
                    [DocumentId] uniqueidentifier NOT NULL,
                    [Title] nvarchar(200) NOT NULL,
                    [Subject] nvarchar(120) NOT NULL,
                    [Chapter] nvarchar(120) NOT NULL,
                    [Teacher] nvarchar(120) NOT NULL,
                    [Snippet] nvarchar(max) NOT NULL,
                    [Score] int NOT NULL,
                    CONSTRAINT [PK_SourceMatches] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_SourceMatches_ChatTurns_ChatTurnId] FOREIGN KEY ([ChatTurnId]) REFERENCES [dbo].[ChatTurns] ([Id]) ON DELETE CASCADE
                );

                CREATE INDEX [IX_SourceMatches_ChatTurnId] ON [dbo].[SourceMatches] ([ChatTurnId]);
            END

            IF COL_LENGTH(N'[dbo].[SourceMatches]', N'FileName') IS NULL
                ALTER TABLE [dbo].[SourceMatches] ADD [FileName] nvarchar(260) NOT NULL CONSTRAINT [DF_SourceMatches_FileName] DEFAULT N'';

            IF COL_LENGTH(N'[dbo].[SourceMatches]', N'HasOriginalFile') IS NULL
                ALTER TABLE [dbo].[SourceMatches] ADD [HasOriginalFile] bit NOT NULL CONSTRAINT [DF_SourceMatches_HasOriginalFile] DEFAULT 0;

            IF OBJECT_ID(N'[dbo].[SystemSettings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[SystemSettings] (
                    [Key] nvarchar(120) NOT NULL,
                    [Value] nvarchar(400) NOT NULL,
                    [UpdatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_SystemSettings] PRIMARY KEY ([Key])
                );
            END

            IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemSettings] WHERE [Key] = N'ChunkSize')
                INSERT INTO [dbo].[SystemSettings] ([Key], [Value], [UpdatedAt]) VALUES (N'ChunkSize', N'1100', SYSDATETIMEOFFSET());

            IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemSettings] WHERE [Key] = N'ChunkOverlap')
                INSERT INTO [dbo].[SystemSettings] ([Key], [Value], [UpdatedAt]) VALUES (N'ChunkOverlap', N'180', SYSDATETIMEOFFSET());

            IF OBJECT_ID(N'[dbo].[Subjects]', N'U') IS NOT NULL
            BEGIN
                UPDATE document
                SET [Department] = subject.[Department]
                FROM [dbo].[Documents] document
                INNER JOIN [dbo].[Subjects] subject ON subject.[Name] = document.[Subject]
                WHERE subject.[Department] <> N''
                  AND (document.[Department] = N'' OR document.[Department] = document.[Subject]);

                UPDATE chunk
                SET [Department] = subject.[Department]
                FROM [dbo].[DocumentChunks] chunk
                INNER JOIN [dbo].[Subjects] subject ON subject.[Name] = chunk.[Subject]
                WHERE subject.[Department] <> N''
                  AND (chunk.[Department] = N'' OR chunk.[Department] = chunk.[Subject]);
            END
            """,
            cancellationToken);
    }

    private static async Task<ChunkSettings> ReadChunkSettingsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var values = await db.SystemSettings
            .AsNoTracking()
            .Where(setting => setting.Key == ChunkSizeSettingKey || setting.Key == ChunkOverlapSettingKey)
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var chunkSize = ParsePositiveSetting(values.GetValueOrDefault(ChunkSizeSettingKey), DefaultChunkSize);
        var chunkOverlap = ParsePositiveSetting(values.GetValueOrDefault(ChunkOverlapSettingKey), DefaultChunkOverlap);
        chunkOverlap = Math.Min(chunkOverlap, Math.Max(0, chunkSize - 1));

        return new ChunkSettings(chunkSize, chunkOverlap);
    }

    private static async Task UpsertSettingAsync(AppDbContext db, string key, string value, CancellationToken cancellationToken)
    {
        var setting = await db.SystemSettings.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (setting is null)
        {
            db.SystemSettings.Add(new SystemSettingEntity
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTimeOffset.Now
            });
            return;
        }

        setting.Value = value;
        setting.UpdatedAt = DateTimeOffset.Now;
    }

    private static int ParsePositiveSetting(string? value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static List<string> ChunkText(string content, ChunkSettings settings)
    {
        var normalized = Regex.Replace(content.Trim(), @"\r\n?", "\n");
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var paragraphs = Regex.Split(normalized, @"\n{2,}")
            .Select(paragraph => Regex.Replace(paragraph.Trim(), @"\s+", " "))
            .Where(paragraph => paragraph.Length > 0)
            .ToList();

        if (paragraphs.Count == 0)
        {
            paragraphs.Add(normalized);
        }

        var chunks = new List<string>();
        var builder = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length > settings.ChunkSize)
            {
                FlushChunk(builder, chunks);
                chunks.AddRange(SplitLongParagraph(paragraph, settings));
                continue;
            }

            if (builder.Length > 0 && builder.Length + paragraph.Length + 1 > settings.ChunkSize)
            {
                FlushChunk(builder, chunks);
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(paragraph);
        }

        FlushChunk(builder, chunks);
        return chunks;
    }

    private static IEnumerable<string> SplitLongParagraph(string paragraph, ChunkSettings settings)
    {
        var start = 0;
        while (start < paragraph.Length)
        {
            var length = Math.Min(settings.ChunkSize, paragraph.Length - start);
            yield return paragraph.Substring(start, length).Trim();

            if (start + length >= paragraph.Length)
            {
                break;
            }

            start += Math.Max(1, settings.ChunkSize - settings.ChunkOverlap);
        }
    }

    private static void FlushChunk(StringBuilder builder, List<string> chunks)
    {
        if (builder.Length == 0)
        {
            return;
        }

        chunks.Add(builder.ToString().Trim());
        builder.Clear();
    }

    private static float[] EmbedText(string value)
    {
        var vector = new float[EmbeddingDimensions];

        foreach (var token in Tokenize(value))
        {
            var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(token);
            var index = Math.Abs(hash % EmbeddingDimensions);
            vector[index] += 1f;
        }

        Normalize(vector);
        return vector;
    }

    private static void Normalize(float[] vector)
    {
        var magnitude = Math.Sqrt(vector.Sum(value => value * value));
        if (magnitude == 0)
        {
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / magnitude);
        }
    }

    private static float CosineSimilarity(float[] left, float[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        var score = 0f;

        for (var i = 0; i < length; i++)
        {
            score += left[i] * right[i];
        }

        return score;
    }

    private static string SerializeEmbedding(float[] embedding)
    {
        return JsonSerializer.Serialize(embedding, EmbeddingJsonOptions);
    }

    private static float[] DeserializeEmbedding(string embedding)
    {
        return JsonSerializer.Deserialize<float[]>(embedding, EmbeddingJsonOptions) ?? new float[EmbeddingDimensions];
    }

    private static string MakeSnippet(string content, string question)
    {
        var tokens = Tokenize(question);
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
            document.UploadedByUserId,
            document.Content,
            document.UploadedAt,
            document.FileContent is { Length: > 0 });
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
                    source.Score,
                    source.FileName,
                    source.HasOriginalFile))
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
            UploadedByUserId = document.UploadedByUserId,
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
                Score = source.Score,
                FileName = source.FileName,
                HasOriginalFile = source.HasOriginalFile
            }).ToList()
        };
    }
}
