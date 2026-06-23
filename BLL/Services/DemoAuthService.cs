using BLL.Models;

namespace BLL.Services;

public sealed class DemoAuthService
{
    private readonly object _lock = new();
    private readonly List<DemoUser> _users =
    [
        new("admin", "123", "Admin", "Admin", false, ""),
        new("gv_prn", "123", "Nguyen Van A", "Teacher", true, "PRN222"),
        new("gv_ai", "123", "Le Van C", "Teacher", true, "AI"),
        new("gv_thuong", "123", "Tran Thi B", "Teacher", false, "PRN222"),
        new("hocsinh", "123", "Sinh vien Demo", "Student", false, "")
    ];
    private readonly List<string> _subjects =
    [
        "Razor Pages",
        "SignalR",
        "RAG"
    ];
    private readonly Dictionary<string, List<string>> _teacherSubjects = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gv_prn"] = ["Razor Pages"],
        ["gv_ai"] = ["RAG"],
        ["gv_thuong"] = ["SignalR"]
    };

    public IReadOnlyList<DemoUser> Users
    {
        get
        {
            lock (_lock)
            {
                return _users.ToList();
            }
        }
    }

    public IReadOnlyList<string> Subjects
    {
        get
        {
            lock (_lock)
            {
                return _subjects.OrderBy(subject => subject).ToList();
            }
        }
    }

    public DemoUser? Validate(string username, string password)
    {
        lock (_lock)
        {
            return _users.FirstOrDefault(user =>
                string.Equals(user.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)
                && user.Password == password);
        }
    }

    public DemoUser? GetUser(string username)
    {
        lock (_lock)
        {
            return _users.FirstOrDefault(user =>
                string.Equals(user.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    public RegisterResult RegisterStudent(string username, string password, string displayName)
    {
        username = username.Trim();
        displayName = displayName.Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(displayName))
        {
            return RegisterResult.Invalid;
        }

        lock (_lock)
        {
            if (_users.Any(user => string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                return RegisterResult.DuplicateUsername;
            }

            _users.Add(new DemoUser(username, password, displayName, "Student", false, ""));
            return RegisterResult.Success;
        }
    }

    public bool UpdateUserRole(string username, string role, bool isDepartmentHead, string managedDepartment)
    {
        username = username.Trim();
        role = role.Trim();
        managedDepartment = managedDepartment.Trim();

        if (!IsAllowedRole(role))
        {
            return false;
        }

        lock (_lock)
        {
            var index = _users.FindIndex(user => string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            _users[index] = _users[index] with
            {
                Role = role,
                IsDepartmentHead = false,
                ManagedDepartment = ""
            };

            if (role != "Teacher")
            {
                _teacherSubjects.Remove(username);
            }

            return true;
        }
    }

    public bool CreateSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return false;
        }

        subject = subject.Trim();

        lock (_lock)
        {
            if (_subjects.Any(item => string.Equals(item, subject, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            _subjects.Add(subject);
            return true;
        }
    }

    public string? GetSubjectOwner(string? subject)
    {
        subject = subject?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        lock (_lock)
        {
            return _teacherSubjects
                .Where(item => item.Value.Any(value => string.Equals(value, subject, StringComparison.OrdinalIgnoreCase)))
                .Select(item => item.Key)
                .FirstOrDefault();
        }
    }

    public bool IsSubjectAssignedToOtherTeacher(string username, string? subject)
    {
        username = username.Trim();
        var owner = GetSubjectOwner(subject);
        return !string.IsNullOrWhiteSpace(owner)
            && !string.Equals(owner, username, StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> GetUnassignedSubjects(IEnumerable<string> subjects)
    {
        lock (_lock)
        {
            return subjects
                .Where(subject => !string.IsNullOrWhiteSpace(subject))
                .Where(subject => !_teacherSubjects.Values.Any(values =>
                    values.Any(value => string.Equals(value, subject, StringComparison.OrdinalIgnoreCase))))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(subject => subject)
                .ToList();
        }
    }

    public bool AssignTeacherSubject(string username, string? subject)
    {
        username = username.Trim();
        subject = subject?.Trim() ?? string.Empty;

        lock (_lock)
        {
            var user = _users.FirstOrDefault(item => string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase));
            if (user is null || user.Role != "Teacher")
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(subject))
            {
                _teacherSubjects.Remove(username);
                return true;
            }

            var subjectOwner = _teacherSubjects
                .Where(item => !string.Equals(item.Key, username, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(item => item.Value.Any(value => string.Equals(value, subject, StringComparison.OrdinalIgnoreCase)));

            if (!string.IsNullOrWhiteSpace(subjectOwner.Key))
            {
                return false;
            }

            if (!_subjects.Any(item => string.Equals(item, subject, StringComparison.OrdinalIgnoreCase)))
            {
                _subjects.Add(subject);
            }

            _teacherSubjects[username] = [subject];
            return true;
        }
    }

    public IReadOnlyList<string> GetTeacherSubjects(string username)
    {
        lock (_lock)
        {
            return _teacherSubjects.TryGetValue(username.Trim(), out var subjects)
                ? subjects.OrderBy(subject => subject).ToList()
                : [];
        }
    }

    private static bool IsAllowedRole(string role)
    {
        return role is "Admin" or "Teacher" or "Student";
    }
}

public enum RegisterResult
{
    Success,
    Invalid,
    DuplicateUsername
}
