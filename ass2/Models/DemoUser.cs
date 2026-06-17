namespace ass2.Models;

public sealed record DemoUser(
    string Username,
    string Password,
    string DisplayName,
    string Role,
    bool IsDepartmentHead,
    string ManagedDepartment);
