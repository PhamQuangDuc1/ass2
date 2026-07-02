using BLL.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ass2.Pages;

public class RegisterModel(DemoAuthService authService) : PageModel
{
    public string? RegisterErrorMessage { get; private set; }
    public string? RegisterMessage { get; private set; }
    public IReadOnlyList<SubjectInfo> SubjectCatalog => authService.SubjectCatalog;
    public IReadOnlyList<string> DepartmentOptions => authService.Departments;

    [BindProperty]
    public string RegisterUsername { get; set; } = string.Empty;

    [BindProperty]
    public string RegisterPassword { get; set; } = string.Empty;

    [BindProperty]
    public string RegisterDisplayName { get; set; } = string.Empty;

    [BindProperty]
    public string RegisterRole { get; set; } = "Student";

    [BindProperty]
    public string RegisterManagedDepartment { get; set; } = string.Empty;

    [BindProperty]
    public List<string> RegisterSubjects { get; set; } = [];

    public void OnGet()
    {
    }

    public IActionResult OnPost()
    {
        var result = authService.RegisterUser(
            RegisterUsername,
            RegisterPassword,
            RegisterDisplayName,
            RegisterRole,
            RegisterManagedDepartment,
            RegisterSubjects);

        if (result == RegisterResult.Success)
        {
            RegisterMessage = "\u0110\u0103ng k\u00fd th\u00e0nh c\u00f4ng. T\u00e0i kho\u1ea3n m\u1edbi \u0111\u00e3 \u0111\u01b0\u1ee3c set role.";
            RegisterPassword = string.Empty;
            RegisterSubjects = [];
            return Page();
        }

        RegisterErrorMessage = result == RegisterResult.DuplicateUsername
            ? "Username \u0111\u00e3 t\u1ed3n t\u1ea1i."
            : "Vui l\u00f2ng nh\u1eadp \u0111\u1ea7y \u0111\u1ee7 th\u00f4ng tin v\u00e0 ch\u1ecdn role h\u1ee3p l\u1ec7.";
        return Page();
    }
}
