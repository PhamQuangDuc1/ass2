using System.Security.Claims;
using ass2.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ass2.Pages;

public class LoginModel(DemoAuthService authService) : PageModel
{
    public IReadOnlyList<Models.DemoUser> DemoUsers => authService.Users;
    public string? ErrorMessage { get; private set; }

    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = authService.Validate(Username, Password);
        if (user is null)
        {
            ErrorMessage = "Sai username hoac password.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.NameIdentifier, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("IsDepartmentHead", user.IsDepartmentHead ? "true" : "false"),
            new("ManagedDepartment", user.ManagedDepartment)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(6)
            });

        return RedirectToPage("/Index");
    }
}
