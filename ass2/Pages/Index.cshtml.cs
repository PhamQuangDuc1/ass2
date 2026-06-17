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
public class IndexModel(KnowledgeBaseService knowledgeBase, IHubContext<ChatHub> hubContext) : PageModel
{
    public IReadOnlyList<KnowledgeDocument> Documents { get; private set; } = [];
    public IReadOnlyDictionary<string, int> SubjectCounts { get; private set; } = new Dictionary<string, int>();
    public string SessionId { get; private set; } = string.Empty;
    public string CurrentUserName { get; private set; } = string.Empty;
    public string CurrentRole { get; private set; } = string.Empty;
    public bool CurrentUserIsDepartmentHead { get; private set; }
    public string CurrentManagedDepartment { get; private set; } = string.Empty;
    public bool CanSeeUploadTab => CurrentRole == "Admin" || (CurrentRole == "Teacher" && CurrentUserIsDepartmentHead);
    public string? StatusMessage { get; private set; }
    public string? ErrorMessage { get; private set; }

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

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPageDataAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        if (!CanUploadDocument())
        {
            ErrorMessage = BuildPermissionMessage();
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

        StatusMessage = $"Da upload va index tai lieu: {document.Title}.";
        await hubContext.Clients.All.SendAsync("DocumentUploaded", new
        {
            document.Title,
            document.Subject,
            document.Teacher,
            UploadedAt = document.UploadedAt.ToString("HH:mm:ss")
        }, cancellationToken);

        ModelState.Clear();
        Title = string.Empty;
        ManualContent = string.Empty;
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

        return CurrentUserIsDepartmentHead
            && string.Equals(CurrentManagedDepartment.Trim(), Department.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private string BuildPermissionMessage()
    {
        if (CurrentRole == "Student")
        {
            return "Hoc sinh chi duoc chat va xem tai lieu, khong duoc upload.";
        }

        if (CurrentRole == "Teacher" && !CurrentUserIsDepartmentHead)
        {
            return "Giao vien thuong khong duoc upload. Chi truong bo mon moi duoc upload tai lieu.";
        }

        if (CurrentRole == "Teacher")
        {
            return $"Ban la truong bo mon {CurrentManagedDepartment}, chi duoc upload tai lieu cua bo mon {CurrentManagedDepartment}.";
        }

        return "Vai tro hien tai khong co quyen upload tai lieu.";
    }

    private async Task LoadPageDataAsync(CancellationToken cancellationToken)
    {
        LoadCurrentUser();
        if (CurrentRole == "Teacher" && CurrentUserIsDepartmentHead && !HttpContext.Request.HasFormContentType)
        {
            Department = CurrentManagedDepartment;
        }

        Documents = await knowledgeBase.GetDocumentsAsync(cancellationToken);
        SubjectCounts = await knowledgeBase.SubjectCountsAsync(cancellationToken);
        SessionId = Request.Cookies["chat-session-id"] ?? Guid.NewGuid().ToString("N");
        Response.Cookies.Append("chat-session-id", SessionId, new CookieOptions
        {
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.Now.AddDays(7)
        });
    }

    private void LoadCurrentUser()
    {
        CurrentUserName = User.Identity?.Name ?? "Unknown";
        CurrentRole = User.FindFirstValue(ClaimTypes.Role) ?? "Student";
        CurrentUserIsDepartmentHead = string.Equals(User.FindFirstValue("IsDepartmentHead"), "true", StringComparison.OrdinalIgnoreCase);
        CurrentManagedDepartment = User.FindFirstValue("ManagedDepartment") ?? string.Empty;
    }
}
