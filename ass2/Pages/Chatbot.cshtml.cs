using ass2.Hubs;
using BLL.Models;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace ass2.Pages;

[Authorize]
public class ChatbotModel(
    KnowledgeBaseService knowledgeBase,
    BenchmarkService benchmarkService,
    DemoAuthService authService,
    IHubContext<ChatHub> chatHub) : PageModel
{
    private const string EditDocumentsTab = "editDocumentsTab";
    public IReadOnlyList<KnowledgeDocument> Documents { get; private set; } = [];
    public IReadOnlyDictionary<Guid, IReadOnlyList<KnowledgeDocumentChunk>> DocumentChunks { get; private set; } = new Dictionary<Guid, IReadOnlyList<KnowledgeDocumentChunk>>();
    public IReadOnlyList<ChatTurn> InitialHistory { get; private set; } = [];
    public IReadOnlyList<DemoUser> Users { get; private set; } = [];
    public IReadOnlyList<string> UploadSubjectOptions { get; private set; } = [];
    public IReadOnlyList<string> VisibleSubjectOptions { get; private set; } = [];
    public IReadOnlyList<string> SubjectOptions { get; private set; } = [];
    public IReadOnlyList<string> DepartmentOptions { get; private set; } = [];
    public IReadOnlyList<SubjectInfo> SubjectCatalog { get; private set; } = [];
    public IReadOnlyList<string> ChapterOptions { get; private set; } = [];
    public IReadOnlyList<SubjectChapterSummary> SubjectChapterSummaries { get; private set; } = [];
    public IReadOnlyDictionary<string, int> SubjectCounts { get; private set; } = new Dictionary<string, int>();
    public BenchmarkDashboard? BenchmarkResults { get; private set; }
    public ChunkSettings CurrentChunkSettings { get; private set; } = new(1100, 180);
    public string SessionId { get; private set; } = string.Empty;
    public string CurrentUserName { get; private set; } = string.Empty;
    public string CurrentUsername { get; private set; } = string.Empty;
    public string CurrentRole { get; private set; } = string.Empty;
    public bool CurrentUserIsDepartmentHead { get; private set; }
    public string CurrentManagedDepartment { get; private set; } = string.Empty;
    public bool CanSeeUploadTab => CurrentRole == "Teacher";
    public string? StatusMessage { get; private set; }
    public string? ErrorMessage { get; private set; }
    [BindProperty(SupportsGet = true)]
    public string ActiveTab { get; set; } = "chatTab";

    [BindProperty]
    public string Question { get; set; } = string.Empty;

    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    [BindProperty]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    public string Department { get; set; } = "PRN222";

    [BindProperty]
    public string Subject { get; set; } = "Razor Pages";

    [BindProperty]
    public string Chapter { get; set; } = "Chapter 1";

    [BindProperty]
    public string Teacher { get; set; } = "Nguyen Van A";

    [BindProperty]
    public string ManualContent { get; set; } = string.Empty;

    [BindProperty]
    public string RoleUsername { get; set; } = string.Empty;

    [BindProperty]
    public string Role { get; set; } = "Student";

    [BindProperty]
    public bool IsDepartmentHead { get; set; }

    [BindProperty]
    public string ManagedDepartment { get; set; } = string.Empty;

    [BindProperty]
    public string NewSubject { get; set; } = string.Empty;

    [BindProperty]
    public string NewDepartment { get; set; } = string.Empty;

    [BindProperty]
    public string AssignTeacherUsername { get; set; } = string.Empty;

    [BindProperty]
    public string AssignSubject { get; set; } = string.Empty;

    [BindProperty]
    public List<string> AssignSubjects { get; set; } = [];

    [BindProperty]
    public Guid DocumentId { get; set; }

    [BindProperty]
    public int ChunkSize { get; set; } = 1100;

    [BindProperty]
    public int ChunkOverlap { get; set; } = 180;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ActiveTab = NormalizeTab(ActiveTab);
        await LoadPageDataAsync(cancellationToken);
    }

    public async Task<IActionResult> OnGetDownloadDocumentAsync(Guid id, CancellationToken cancellationToken)
    {
        var original = await knowledgeBase.GetOriginalFileAsync(id, cancellationToken);
        return original is null
            ? NotFound()
            : File(original.Content, string.IsNullOrWhiteSpace(original.ContentType) ? "application/octet-stream" : original.ContentType, original.FileName);
    }

    public async Task<IActionResult> OnPostRunBenchmarkAsync(CancellationToken cancellationToken)
    {
        BenchmarkResults = await benchmarkService.RunAsync(cancellationToken);
        ActiveTab = "benchmarkTab";
        StatusMessage = "Da chay benchmark tren tap tai lieu hien tai.";
        await LoadPageDataAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAskAsync(CancellationToken cancellationToken)
    {
        ActiveTab = "chatTab";
        await LoadPageDataAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(Question))
        {
            ErrorMessage = "Hay nhap cau hoi truoc khi gui.";
            return Page();
        }

        await knowledgeBase.AskAsync(SessionId, Question.Trim(), cancellationToken);
        Question = string.Empty;
        ModelState.Clear();
        await LoadPageDataAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        if (!CanUploadDocument())
        {
            ErrorMessage = BuildPermissionMessage();
            ActiveTab = EditDocumentsTab;
            await LoadPageDataAsync(cancellationToken);
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            Title = UploadFile?.FileName ?? "Tai lieu moi";
        }

        var document = await knowledgeBase.AddDocumentAsync(
            UploadFile,
            Title,
            authService.GetSubjectDepartment(Subject),
            Subject,
            Chapter,
            CurrentUserName,
            CurrentUserName,
            CurrentUsername,
            ManualContent,
            cancellationToken);

        await chatHub.Clients.All.SendAsync(
            "DocumentCreated",
            new DocumentCreated(await ToDocumentChangedPayloadAsync(document, cancellationToken)),
            cancellationToken);

        StatusMessage = $"Da upload va index tai lieu: {document.Title}.";
        ModelState.Clear();
        Title = string.Empty;
        ManualContent = string.Empty;
        ActiveTab = EditDocumentsTab;
        await LoadPageDataAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateDocumentAsync(CancellationToken cancellationToken)
    {
        ActiveTab = EditDocumentsTab;
        var existing = await knowledgeBase.GetDocumentAsync(DocumentId, cancellationToken);
        if (string.IsNullOrWhiteSpace(Chapter) || !CanEditDocument(existing, Subject))
        {
            ErrorMessage = BuildPermissionMessage();
            await LoadPageDataAsync(cancellationToken);
            return Page();
        }

        var subject = string.IsNullOrWhiteSpace(Subject) ? existing!.Subject : Subject.Trim();
        var department = authService.GetSubjectDepartment(subject);
        var document = await knowledgeBase.UpdateDocumentAsync(
            DocumentId,
            UploadFile,
            Title,
            department,
            subject,
            Chapter,
            CurrentUserName,
            ManualContent,
            cancellationToken);

        if (document is null)
        {
            ErrorMessage = "Khong tim thay tai lieu can cap nhat.";
            await LoadPageDataAsync(cancellationToken);
            return Page();
        }

        await chatHub.Clients.All.SendAsync(
            "DocumentUpdated",
            new DocumentUpdated(await ToDocumentChangedPayloadAsync(document, cancellationToken)),
            cancellationToken);

        StatusMessage = $"Da cap nhat tai lieu: {document.Title}.";
        ModelState.Clear();
        await LoadPageDataAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteDocumentAsync(CancellationToken cancellationToken)
    {
        ActiveTab = EditDocumentsTab;
        var existing = await knowledgeBase.GetDocumentAsync(DocumentId, cancellationToken);
        if (!CanEditDocument(existing))
        {
            ErrorMessage = BuildPermissionMessage();
            await LoadPageDataAsync(cancellationToken);
            return Page();
        }

        if (await knowledgeBase.DeleteDocumentAsync(DocumentId, cancellationToken))
        {
            await chatHub.Clients.All.SendAsync(
                "DocumentDeleted",
                new DocumentDeleted(existing!.Id, existing.Title, existing.Department, existing.Subject, CurrentUsername),
                cancellationToken);
            StatusMessage = $"Da xoa tai lieu: {existing!.Title}.";
        }
        else
        {
            ErrorMessage = "Khong tim thay tai lieu can xoa.";
        }

        await LoadPageDataAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostReindexDocumentAsync(CancellationToken cancellationToken)
    {
        ActiveTab = EditDocumentsTab;
        var existing = await knowledgeBase.GetDocumentAsync(DocumentId, cancellationToken);
        if (!CanEditDocument(existing))
        {
            ErrorMessage = BuildPermissionMessage();
            await LoadPageDataAsync(cancellationToken);
            return Page();
        }

        var document = await knowledgeBase.ReindexDocumentAsync(DocumentId, cancellationToken);
        if (document is null)
        {
            ErrorMessage = "Khong tim thay tai lieu can re-index.";
            await LoadPageDataAsync(cancellationToken);
            return Page();
        }

        await chatHub.Clients.All.SendAsync(
            "DocumentReindexed",
            new DocumentReindexed(await ToDocumentChangedPayloadAsync(document, cancellationToken)),
            cancellationToken);

        StatusMessage = $"Da re-index tai lieu: {document.Title}.";
        await LoadPageDataAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateChunkSettingsAsync(CancellationToken cancellationToken)
    {
        LoadCurrentUser();
        ActiveTab = "adminTab";
        if (CurrentRole != "Admin")
        {
            ErrorMessage = "Chi Admin moi duoc cau hinh chunk.";
            await LoadPageDataAsync(cancellationToken);
            return Page();
        }

        CurrentChunkSettings = await knowledgeBase.UpdateChunkSettingsAsync(ChunkSize, ChunkOverlap, cancellationToken);
        StatusMessage = $"Da cap nhat chunk size {CurrentChunkSettings.ChunkSize}, overlap {CurrentChunkSettings.ChunkOverlap}.";
        await LoadPageDataAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateSubjectAsync(CancellationToken cancellationToken)
    {
        LoadCurrentUser();
        if (CurrentRole != "Admin")
        {
            ErrorMessage = "Chi Admin moi duoc tao mon hoc moi.";
            ActiveTab = "subjectsTab";
            await LoadPageDataAsync(cancellationToken);
            return Page();
        }

        if (authService.CreateSubject(NewSubject, NewDepartment))
        {
            StatusMessage = $"Da tao mon hoc {NewSubject.Trim()} trong bo mon {NewDepartment.Trim()}.";
        }
        else
        {
            ErrorMessage = "Khong tao duoc mon hoc. Ten mon/bo mon co the dang trong hoac mon da ton tai.";
        }

        ActiveTab = "subjectsTab";
        await LoadPageDataAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAssignSubjectAsync(CancellationToken cancellationToken)
    {
        LoadCurrentUser();
        if (CurrentRole != "Admin")
        {
            ErrorMessage = "Chi Admin moi duoc gan mon cho giao vien.";
            ActiveTab = "subjectsTab";
            await LoadPageDataAsync(cancellationToken);
            return Page();
        }

        if (authService.AssignTeacherSubject(AssignTeacherUsername, AssignSubject))
        {
            StatusMessage = string.IsNullOrWhiteSpace(AssignSubject)
                ? $"Da bo gan mon cho giao vien {AssignTeacherUsername}."
                : $"Da gan mon {AssignSubject} cho giao vien {AssignTeacherUsername}.";
        }
        else
        {
            ErrorMessage = "Khong gan duoc mon. Hay kiem tra tai khoan giao vien va mon hoc.";
        }

        ActiveTab = "subjectsTab";
        await LoadPageDataAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateRoleAsync(CancellationToken cancellationToken)
    {
        LoadCurrentUser();
        if (CurrentRole != "Admin")
        {
            ErrorMessage = "Chi Admin moi duoc set role tai khoan.";
            ActiveTab = "adminTab";
            await LoadPageDataAsync(cancellationToken);
            return Page();
        }

        if (authService.UpdateUserRole(RoleUsername, Role, false, ManagedDepartment))
        {
            if (Role == "Teacher" && !authService.AssignTeacherSubjects(RoleUsername, AssignSubjects))
            {
                ErrorMessage = "Da cap nhat role nhung khong gan duoc mon. Hay kiem tra tai khoan giao vien va mon hoc.";
            }
            else
            {
                StatusMessage = $"Da cap nhat role cho tai khoan {RoleUsername}.";
            }
        }
        else
        {
            ErrorMessage = "Khong cap nhat duoc role. Kiem tra username hoac role.";
        }

        ActiveTab = "adminTab";
        await LoadPageDataAsync(cancellationToken);
        return Page();
    }

    private bool CanUploadDocument()
    {
        LoadCurrentUser();

        if (CurrentRole != "Teacher")
        {
            return false;
        }

        var department = authService.GetSubjectDepartment(Subject);
        var subjectDepartment = authService.GetSubjectDepartment(Subject);
        var assignedSubjects = authService.GetTeacherSubjects(CurrentUsername);

        return !string.IsNullOrWhiteSpace(department)
            && !string.IsNullOrWhiteSpace(Chapter)
            && assignedSubjects.Contains(Subject, StringComparer.OrdinalIgnoreCase)
            && string.Equals(subjectDepartment, department, StringComparison.OrdinalIgnoreCase);
    }

    private string BuildPermissionMessage()
    {
        return "Bạn không có quyền chỉnh sửa tài liệu này.";
    }

    private bool CanEditDocument(KnowledgeDocument? document, string? newSubject = null)
    {
        LoadCurrentUser();
        if (document is null || CurrentRole != "Teacher")
        {
            return false;
        }

        if (!string.Equals(document.UploadedByUserId, CurrentUsername, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var assignedSubjects = authService.GetTeacherSubjects(CurrentUsername);
        if (!assignedSubjects.Contains(document.Subject, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var targetSubject = string.IsNullOrWhiteSpace(newSubject) ? document.Subject : newSubject.Trim();
        return assignedSubjects.Contains(targetSubject, StringComparer.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(authService.GetSubjectDepartment(targetSubject));
    }

    private async Task LoadPageDataAsync(CancellationToken cancellationToken)
    {
        LoadCurrentUser();
        if (CurrentRole == "Teacher" && !HttpContext.Request.HasFormContentType)
        {
            var firstSubject = authService.GetTeacherSubjects(CurrentUsername).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstSubject))
            {
                Subject = firstSubject;
                Department = authService.GetSubjectDepartment(firstSubject);
            }
            Teacher = CurrentUserName;
        }

        Documents = await knowledgeBase.GetDocumentsAsync(cancellationToken);
        DocumentChunks = await knowledgeBase.GetChunksByDocumentAsync(Documents.Select(document => document.Id), cancellationToken);
        CurrentChunkSettings = await knowledgeBase.GetChunkSettingsAsync(cancellationToken);
        ChunkSize = CurrentChunkSettings.ChunkSize;
        ChunkOverlap = CurrentChunkSettings.ChunkOverlap;
        SubjectCatalog = authService.SubjectCatalog;
        DepartmentOptions = Documents
            .Select(document => document.Department)
            .Concat(authService.Departments)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();
        SubjectOptions = Documents
            .Select(document => document.Subject)
            .Concat(authService.Subjects)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();
        UploadSubjectOptions = CurrentRole == "Teacher"
            ? authService.GetTeacherSubjects(CurrentUsername)
            : [];
        VisibleSubjectOptions = CurrentRole == "Teacher"
            ? authService.GetTeacherSubjects(CurrentUsername)
            : [];
        ChapterOptions = Documents
            .Select(document => document.Chapter)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();
        SubjectChapterSummaries = Documents
            .GroupBy(document => new { document.Subject, document.Chapter })
            .Select(group => new SubjectChapterSummary(group.Key.Subject, group.Key.Chapter, group.Count()))
            .OrderBy(summary => summary.Subject)
            .ThenBy(summary => summary.Chapter)
            .ToList();
        SubjectCounts = await knowledgeBase.SubjectCountsAsync(cancellationToken);
        Users = authService.Users
            .OrderBy(user => user.Username)
            .ToList();
        SessionId = Request.Cookies["chat-session-id"] ?? Guid.NewGuid().ToString("N");
        Response.Cookies.Append("chat-session-id", SessionId, new CookieOptions
        {
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.Now.AddDays(7)
        });
        InitialHistory = await knowledgeBase.GetHistoryAsync(SessionId, cancellationToken);
    }

    private void LoadCurrentUser()
    {
        CurrentUsername = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var currentUser = authService.GetUser(CurrentUsername);

        CurrentUserName = currentUser?.DisplayName ?? User.Identity?.Name ?? "Unknown";
        CurrentRole = currentUser?.Role ?? User.FindFirstValue(ClaimTypes.Role) ?? "Student";
        CurrentUserIsDepartmentHead = currentUser?.IsDepartmentHead
            ?? string.Equals(User.FindFirstValue("IsDepartmentHead"), "true", StringComparison.OrdinalIgnoreCase);
        CurrentManagedDepartment = currentUser?.ManagedDepartment ?? User.FindFirstValue("ManagedDepartment") ?? string.Empty;
    }

    public IReadOnlyList<string> GetAssignedSubjects(string username)
    {
        return authService.GetTeacherSubjects(username);
    }

    public bool CanAssignSubjectToTeacher(string username, string subject)
    {
        return true;
    }

    public bool CanManageDocument(KnowledgeDocument document)
    {
        return CanEditDocument(document);
    }

    public string GetSubjectOwnerLabel(string subject)
    {
        var ownerUsernames = authService.GetSubjectOwners(subject);
        if (ownerUsernames.Count == 0)
        {
            return "Chua co giao vien";
        }

        return string.Join(", ", ownerUsernames.Select(ownerUsername =>
        {
            return GetUserLabel(ownerUsername);
        }));
    }

    public string GetUserLabel(string username)
    {
        var user = authService.GetUser(username);
        return user is null
            ? username
            : $"{user.DisplayName} ({user.Username})";
    }

    public IReadOnlyList<KnowledgeDocumentChunk> GetDocumentChunks(Guid documentId)
    {
        return DocumentChunks.TryGetValue(documentId, out var chunks)
            ? chunks
            : [];
    }

    private static string NormalizeTab(string? tab)
    {
        return tab switch
        {
            "chatTab" or "documentsTab" or EditDocumentsTab or "adminTab" or "subjectsTab" or "benchmarkTab" => tab,
            "uploadTab" => EditDocumentsTab,
            _ => "chatTab"
        };
    }

    private async Task<DocumentChangedPayload> ToDocumentChangedPayloadAsync(KnowledgeDocument document, CancellationToken cancellationToken)
    {
        var chunks = await knowledgeBase.GetChunksByDocumentAsync([document.Id], cancellationToken);
        var chunkCount = chunks.TryGetValue(document.Id, out var documentChunks)
            ? documentChunks.Count
            : 0;

        return new DocumentChangedPayload(
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
            document.UploadedAt.ToString("dd/MM HH:mm"),
            document.HasOriginalFile,
            $"/Chatbot?handler=DownloadDocument&id={document.Id}",
            chunkCount);
    }
}

public sealed record SubjectChapterSummary(string Subject, string Chapter, int DocumentCount);

