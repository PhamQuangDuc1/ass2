using ass2.Models;

namespace ass2.Services;

public sealed class DemoAuthService
{
    private readonly List<DemoUser> _users =
    [
        new("admin", "123", "Admin", "Admin", false, ""),
        new("gv_prn", "123", "Nguyen Van A", "Teacher", true, "PRN222"),
        new("gv_ai", "123", "Le Van C", "Teacher", true, "AI"),
        new("gv_thuong", "123", "Tran Thi B", "Teacher", false, "PRN222"),
        new("hocsinh", "123", "Sinh vien Demo", "Student", false, "")
    ];

    public IReadOnlyList<DemoUser> Users => _users;

    public DemoUser? Validate(string username, string password)
    {
        return _users.FirstOrDefault(user =>
            string.Equals(user.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)
            && user.Password == password);
    }
}
