using BLL.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ass2.Pages;

public class RegisterModel(DemoAuthService authService) : PageModel
{
    public string? RegisterErrorMessage { get; private set; }
    public string? RegisterMessage { get; private set; }

    [BindProperty]
    public string RegisterUsername { get; set; } = string.Empty;

    [BindProperty]
    public string RegisterPassword { get; set; } = string.Empty;

    [BindProperty]
    public string RegisterDisplayName { get; set; } = string.Empty;

    public void OnGet()
    {
    }

    public IActionResult OnPost()
    {
        var result = authService.RegisterStudent(RegisterUsername, RegisterPassword, RegisterDisplayName);
        if (result == RegisterResult.Success)
        {
            RegisterMessage = "\u0110\u0103ng k\u00fd th\u00e0nh c\u00f4ng. T\u00e0i kho\u1ea3n m\u1edbi m\u1eb7c \u0111\u1ecbnh l\u00e0 h\u1ecdc sinh.";
            RegisterPassword = string.Empty;
            return Page();
        }

        RegisterErrorMessage = result == RegisterResult.DuplicateUsername
            ? "Username \u0111\u00e3 t\u1ed3n t\u1ea1i."
            : "Vui l\u00f2ng nh\u1eadp \u0111\u1ea7y \u0111\u1ee7 username, password v\u00e0 t\u00ean hi\u1ec3n th\u1ecb.";
        return Page();
    }
}
