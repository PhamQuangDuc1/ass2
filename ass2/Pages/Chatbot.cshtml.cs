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
    public string SessionId { get; private set; } = string.Empty;
    public string CurrentUserName { get; private set; } = string.Empty;
    public string CurrentUsername { get; private set; } = string.Empty;
    public string CurrentRole { get; private set; } = string.Empty;
    public bool CurrentUserIsDepartmentHead { get; private set; }
    public string CurrentManagedDepartment { get; private set; } = string.Empty;
    public bool CanSeeUploadTab => CurrentRole == "Teacher" && CurrentUserIsDepartmentHead;
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
            ActiveTab = "uploadTab";
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
            Department,
            Subject,
            Chapter,
            Teacher,
            CurrentUserName,
            ManualContent,
            cancellationToken);

        await chatHub.Clients.All.SendAsync(
            "ReceiveDocumentUploaded",
            ToDocumentUploadedPayload(document),
            cancellationToken);

        StatusMessage = $"Da upload va index tai lieu: {document.Title}.";
        ModelState.Clear();
        Title = string.Empty;
        ManualContent = string.Empty;
        ActiveTab = "uploadTab";
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

        var existingDepartmentHead = IsDepartmentHead
            ? authService.GetDepartmentHead(ManagedDepartment)
            : null;

        if (IsDepartmentHead && string.IsNullOrWhiteSpace(ManagedDepartment))
        {
            ErrorMessage = "Hay chon bo mon khi set truong bo mon.";
        }
        else if (!string.IsNullOrWhiteSpace(existingDepartmentHead)
            && !string.Equals(existingDepartmentHead, RoleUsername, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = $"Bo mon {ManagedDepartment.Trim()} da co truong bo mon: {GetUserLabel(existingDepartmentHead)}.";
        }
        else if (authService.UpdateUserRole(RoleUsername, Role, IsDepartmentHead, ManagedDepartment))
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

        if (CurrentRole != "Teacher" || !CurrentUserIsDepartmentHead)
        {
            return false;
        }

        var department = Department.Trim();
        var subjectDepartment = authService.GetSubjectDepartment(Subject);

        return !string.IsNullOrWhiteSpace(department)
            && string.Equals(department, CurrentManagedDepartment, StringComparison.OrdinalIgnoreCase)
            && string.Equals(subjectDepartment, department, StringComparison.OrdinalIgnoreCase);
    }

    private string BuildPermissionMessage()
    {
        if (CurrentRole == "Student")
        {
            return "Hoc sinh chi duoc chat va xem tai lieu, khong duoc upload.";
        }

        if (CurrentRole == "Teacher")
        {
            if (!CurrentUserIsDepartmentHead)
            {
                return "Chi truong bo mon moi duoc upload tai lieu.";
            }

            return $"Ban chi duoc upload tai lieu thuoc bo mon {CurrentManagedDepartment}.";
        }

        return "Chi truong bo mon moi co quyen upload tai lieu.";
    }

    private async Task LoadPageDataAsync(CancellationToken cancellationToken)
    {
        LoadCurrentUser();
        if (CurrentRole == "Teacher" && CurrentUserIsDepartmentHead && !HttpContext.Request.HasFormContentType)
        {
            Department = CurrentManagedDepartment;
            var firstSubject = authService.GetSubjectsByDepartment(CurrentManagedDepartment).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstSubject))
            {
                Subject = firstSubject;
            }
        }

        Documents = await knowledgeBase.GetDocumentsAsync(cancellationToken);
        DocumentChunks = await knowledgeBase.GetChunksByDocumentAsync(Documents.Select(document => document.Id), cancellationToken);
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
        UploadSubjectOptions = CurrentRole == "Teacher" && CurrentUserIsDepartmentHead
            ? authService.GetSubjectsByDepartment(CurrentManagedDepartment)
            : [];
        VisibleSubjectOptions = CurrentRole == "Teacher"
            ? CurrentUserIsDepartmentHead
                ? authService.GetSubjectsByDepartment(CurrentManagedDepartment)
                : authService.GetTeacherSubjects(CurrentUsername)
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
        return tab is "chatTab" or "documentsTab" or "uploadTab" or "adminTab" or "subjectsTab" or "benchmarkTab"
            ? tab
            : "chatTab";
    }

    private static DocumentUploadedPayload ToDocumentUploadedPayload(KnowledgeDocument document)
    {
        return new DocumentUploadedPayload(
            document.Id,
            document.Title,
            document.FileName,
            document.Department,
            document.Subject,
            document.Chapter,
            document.Teacher,
            document.UploadedBy,
            document.Content,
            document.UploadedAt.ToString("dd/MM HH:mm"),
            document.HasOriginalFile,
            $"/Chatbot?handler=DownloadDocument&id={document.Id}");
    }
}

public sealed record SubjectChapterSummary(string Subject, string Chapter, int DocumentCount);

