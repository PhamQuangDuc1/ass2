using ass2.Hubs;
using ass2.Models;
using ass2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace ass2.Pages;

[Authorize]
public class IndexModel(KnowledgeBaseService knowledgeBase, IHubContext<ChatHub> hubContext, DemoAuthService authService) : PageModel
{
    public IReadOnlyList<KnowledgeDocument> Documents { get; private set; } = [];
    public IReadOnlyList<ChatTurn> InitialHistory { get; private set; } = [];
    public IReadOnlyList<DemoUser> Users { get; private set; } = [];
    public IReadOnlyList<string> UploadSubjectOptions { get; private set; } = [];
    public IReadOnlyList<string> SubjectOptions { get; private set; } = [];
    public IReadOnlyList<string> ChapterOptions { get; private set; } = [];
    public IReadOnlyList<SubjectChapterSummary> SubjectChapterSummaries { get; private set; } = [];
    public IReadOnlyDictionary<string, int> SubjectCounts { get; private set; } = new Dictionary<string, int>();
    public string SessionId { get; private set; } = string.Empty;
    public string CurrentUserName { get; private set; } = string.Empty;
    public string CurrentUsername { get; private set; } = string.Empty;
    public string CurrentRole { get; private set; } = string.Empty;
    public bool CurrentUserIsDepartmentHead { get; private set; }
    public string CurrentManagedDepartment { get; private set; } = string.Empty;
    public bool CanSeeUploadTab => CurrentRole == "Admin" || (CurrentRole == "Teacher" && authService.GetTeacherSubjects(CurrentUsername).Count > 0);
    public string? StatusMessage { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string ActiveTab { get; private set; } = "chatTab";

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
    public string AssignTeacherUsername { get; set; } = string.Empty;

    [BindProperty]
    public string AssignSubject { get; set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPageDataAsync(cancellationToken);
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

        Department = Subject;

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

        StatusMessage = $"Da upload va index tai lieu: {document.Title}.";
        await hubContext.Clients.All.SendAsync("DocumentUploaded", new
        {
            document.Title,
            document.Department,
            document.Subject,
            document.Chapter,
            document.Teacher,
            document.FileName,
            document.UploadedBy,
            document.Content,
            UploadedAt = document.UploadedAt.ToString("HH:mm:ss")
        }, cancellationToken);

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

        if (authService.CreateSubject(NewSubject))
        {
            StatusMessage = $"Da tao mon hoc {NewSubject.Trim()}.";
            await hubContext.Clients.All.SendAsync("SubjectsChanged", new
            {
                Subject = NewSubject.Trim()
            }, cancellationToken);
        }
        else
        {
            ErrorMessage = "Khong tao duoc mon hoc. Ten mon co the dang trong hoac da ton tai.";
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
            StatusMessage = $"Da gan mon {AssignSubject} cho giao vien {AssignTeacherUsername}.";
            await NotifyAccountChangedAsync(AssignTeacherUsername, cancellationToken);
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

        if (authService.UpdateUserRole(RoleUsername, Role, false, ""))
        {
            if (Role == "Teacher")
            {
                authService.AssignTeacherSubject(RoleUsername, AssignSubject);
            }

            StatusMessage = $"Da cap nhat role cho tai khoan {RoleUsername}.";
            await NotifyAccountChangedAsync(RoleUsername, cancellationToken);
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

        if (CurrentRole == "Admin")
        {
            return true;
        }

        if (CurrentRole != "Teacher")
        {
            return false;
        }

        return authService.GetTeacherSubjects(CurrentUsername)
                .Any(subject => string.Equals(subject, Subject.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private string BuildPermissionMessage()
    {
        if (CurrentRole == "Student")
        {
            return "Hoc sinh chi duoc chat va xem tai lieu, khong duoc upload.";
        }

        if (CurrentRole == "Teacher")
        {
            var subjects = authService.GetTeacherSubjects(CurrentUsername);
            if (subjects.Count == 0)
            {
                return "Ban chua duoc Admin gan mon hoc de upload.";
            }

            return $"Ban chi duoc upload tai lieu cua mon: {string.Join(", ", subjects)}.";
        }

        return "Vai tro hien tai khong co quyen upload tai lieu.";
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
                Department = firstSubject;
            }
        }

        Documents = await knowledgeBase.GetDocumentsAsync(cancellationToken);
        SubjectOptions = Documents
            .Select(document => document.Subject)
            .Concat(authService.Subjects)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();
        UploadSubjectOptions = CurrentRole == "Admin"
            ? SubjectOptions
            : authService.GetTeacherSubjects(CurrentUsername);
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

    private async Task NotifyAccountChangedAsync(string username, CancellationToken cancellationToken)
    {
        var user = authService.GetUser(username);
        await hubContext.Clients.Group(ChatHub.UserGroup(username)).SendAsync("AccountUpdated", new
        {
            Username = username,
            Role = user?.Role,
            Department = user?.ManagedDepartment,
            IsDepartmentHead = user?.IsDepartmentHead,
            Subjects = authService.GetTeacherSubjects(username)
        }, cancellationToken);
    }
}

public sealed record SubjectChapterSummary(string Subject, string Chapter, int DocumentCount);
