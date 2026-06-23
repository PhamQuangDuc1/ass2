using BLL.Models;
using DAL.Data;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public sealed class DemoAuthService(IDbContextFactory<AppDbContext> dbContextFactory)
{
    private readonly object _lock = new();
    private bool _schemaReady;

    public void EnsureReady()
    {
        lock (_lock)
        {
            using var db = CreateReadyDbContext();
        }
    }

    public IReadOnlyList<DemoUser> Users
    {
        get
        {
            lock (_lock)
            {
                using var db = CreateReadyDbContext();
                return db.Users
                    .AsNoTracking()
                    .OrderBy(user => user.Username)
                    .Select(user => ToModel(user))
                    .ToList();
            }
        }
    }

    public IReadOnlyList<string> Subjects
    {
        get
        {
            lock (_lock)
            {
                using var db = CreateReadyDbContext();
                return db.Subjects
                    .AsNoTracking()
                    .OrderBy(subject => subject.Name)
                    .Select(subject => subject.Name)
                    .ToList();
            }
        }
    }

    public DemoUser? Validate(string username, string password)
    {
        username = username.Trim();
        var normalizedUsername = Normalize(username);

        lock (_lock)
        {
            using var db = CreateReadyDbContext();
            var user = db.Users
                .AsNoTracking()
                .FirstOrDefault(item => item.Username.ToLower() == normalizedUsername && item.Password == password);
            return user is null ? null : ToModel(user);
        }
    }

    public DemoUser? GetUser(string username)
    {
        username = username.Trim();
        var normalizedUsername = Normalize(username);

        lock (_lock)
        {
            using var db = CreateReadyDbContext();
            var user = db.Users
                .AsNoTracking()
                .FirstOrDefault(item => item.Username.ToLower() == normalizedUsername);
            return user is null ? null : ToModel(user);
        }
    }

    public RegisterResult RegisterStudent(string username, string password, string displayName)
    {
        username = username.Trim();
        displayName = displayName.Trim();
        var normalizedUsername = Normalize(username);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(displayName))
        {
            return RegisterResult.Invalid;
        }

        lock (_lock)
        {
            using var db = CreateReadyDbContext();
            if (db.Users.Any(user => user.Username.ToLower() == normalizedUsername))
            {
                return RegisterResult.DuplicateUsername;
            }

            db.Users.Add(new UserEntity
            {
                Username = username,
                Password = password,
                DisplayName = displayName,
                Role = "Student",
                IsDepartmentHead = false,
                ManagedDepartment = string.Empty,
                CreatedAt = DateTimeOffset.Now
            });
            db.SaveChanges();
            return RegisterResult.Success;
        }
    }

    public bool UpdateUserRole(string username, string role, bool isDepartmentHead, string managedDepartment)
    {
        username = username.Trim();
        role = role.Trim();
        managedDepartment = managedDepartment.Trim();
        var normalizedUsername = Normalize(username);

        if (!IsAllowedRole(role))
        {
            return false;
        }

        lock (_lock)
        {
            using var db = CreateReadyDbContext();
            var user = db.Users.FirstOrDefault(item => item.Username.ToLower() == normalizedUsername);
            if (user is null)
            {
                return false;
            }

            user.Role = role;
            user.IsDepartmentHead = false;
            user.ManagedDepartment = string.Empty;

            if (role != "Teacher")
            {
                var assignments = db.TeacherSubjects.Where(item => item.Username.ToLower() == normalizedUsername).ToList();
                db.TeacherSubjects.RemoveRange(assignments);
            }

            db.SaveChanges();
            return true;
        }
    }

    public bool CreateSubject(string? subject)
    {
        subject = subject?.Trim() ?? string.Empty;
        var normalizedSubject = Normalize(subject);

        if (string.IsNullOrWhiteSpace(subject))
        {
            return false;
        }

        lock (_lock)
        {
            using var db = CreateReadyDbContext();
            if (db.Subjects.Any(item => item.Name.ToLower() == normalizedSubject))
            {
                return false;
            }

            db.Subjects.Add(new SubjectEntity
            {
                Name = subject,
                CreatedAt = DateTimeOffset.Now
            });
            db.SaveChanges();
            return true;
        }
    }

    public string? GetSubjectOwner(string? subject)
    {
        subject = subject?.Trim() ?? string.Empty;
        var normalizedSubject = Normalize(subject);

        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        lock (_lock)
        {
            using var db = CreateReadyDbContext();
            return db.TeacherSubjects
                .AsNoTracking()
                .Where(item => item.Subject.ToLower() == normalizedSubject)
                .Select(item => item.Username)
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
            using var db = CreateReadyDbContext();
            var assignedSubjects = db.TeacherSubjects
                .AsNoTracking()
                .Select(item => item.Subject)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return subjects
                .Where(subject => !string.IsNullOrWhiteSpace(subject))
                .Where(subject => !assignedSubjects.Contains(subject))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(subject => subject)
                .ToList();
        }
    }

    public bool AssignTeacherSubject(string username, string? subject)
    {
        username = username.Trim();
        subject = subject?.Trim() ?? string.Empty;
        var normalizedUsername = Normalize(username);
        var normalizedSubject = Normalize(subject);

        lock (_lock)
        {
            using var db = CreateReadyDbContext();
            var user = db.Users.FirstOrDefault(item => item.Username.ToLower() == normalizedUsername);
            if (user is null || user.Role != "Teacher")
            {
                return false;
            }

            var existingTeacherAssignments = db.TeacherSubjects
                .Where(item => item.Username.ToLower() == normalizedUsername)
                .ToList();

            if (string.IsNullOrWhiteSpace(subject))
            {
                db.TeacherSubjects.RemoveRange(existingTeacherAssignments);
                db.SaveChanges();
                return true;
            }

            var subjectAssignment = db.TeacherSubjects.FirstOrDefault(item => item.Subject.ToLower() == normalizedSubject);
            if (subjectAssignment is not null && !string.Equals(subjectAssignment.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!db.Subjects.Any(item => item.Name.ToLower() == normalizedSubject))
            {
                db.Subjects.Add(new SubjectEntity
                {
                    Name = subject,
                    CreatedAt = DateTimeOffset.Now
                });
            }

            foreach (var assignment in existingTeacherAssignments.Where(item => !string.Equals(item.Subject, subject, StringComparison.OrdinalIgnoreCase)))
            {
                db.TeacherSubjects.Remove(assignment);
            }

            if (subjectAssignment is null)
            {
                db.TeacherSubjects.Add(new TeacherSubjectEntity
                {
                    Username = username,
                    Subject = subject,
                    AssignedAt = DateTimeOffset.Now
                });
            }
            else
            {
                subjectAssignment.Username = username;
                subjectAssignment.AssignedAt = DateTimeOffset.Now;
            }

            db.SaveChanges();
            return true;
        }
    }

    public IReadOnlyList<string> GetTeacherSubjects(string username)
    {
        username = username.Trim();
        var normalizedUsername = Normalize(username);

        lock (_lock)
        {
            using var db = CreateReadyDbContext();
            return db.TeacherSubjects
                .AsNoTracking()
                .Where(item => item.Username.ToLower() == normalizedUsername)
                .OrderBy(item => item.Subject)
                .Select(item => item.Subject)
                .ToList();
        }
    }

    private AppDbContext CreateReadyDbContext()
    {
        var db = dbContextFactory.CreateDbContext();
        EnsureSchemaAndSeed(db);
        return db;
    }

    private void EnsureSchemaAndSeed(AppDbContext db)
    {
        if (_schemaReady)
        {
            return;
        }

        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw(
            """
            IF OBJECT_ID(N'[dbo].[Users]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[Users] (
                    [Username] nvarchar(120) NOT NULL,
                    [Password] nvarchar(200) NOT NULL,
                    [DisplayName] nvarchar(160) NOT NULL,
                    [Role] nvarchar(40) NOT NULL,
                    [IsDepartmentHead] bit NOT NULL CONSTRAINT [DF_Users_IsDepartmentHead] DEFAULT 0,
                    [ManagedDepartment] nvarchar(120) NOT NULL CONSTRAINT [DF_Users_ManagedDepartment] DEFAULT N'',
                    [CreatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_Users] PRIMARY KEY ([Username])
                );
            END

            IF OBJECT_ID(N'[dbo].[Subjects]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[Subjects] (
                    [Name] nvarchar(120) NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_Subjects] PRIMARY KEY ([Name])
                );
            END

            IF OBJECT_ID(N'[dbo].[TeacherSubjects]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[TeacherSubjects] (
                    [Subject] nvarchar(120) NOT NULL,
                    [Username] nvarchar(120) NOT NULL,
                    [AssignedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_TeacherSubjects] PRIMARY KEY ([Subject])
                );

                CREATE INDEX [IX_TeacherSubjects_Username] ON [dbo].[TeacherSubjects] ([Username]);
            END
            """);

        SeedDefaultData(db);
        _schemaReady = true;
    }

    private static void SeedDefaultData(AppDbContext db)
    {
        var now = DateTimeOffset.Now;
        var defaultSubjects = new[]
        {
            "Razor Pages",
            "SignalR",
            "RAG"
        };

        foreach (var subject in defaultSubjects)
        {
            var normalizedSubject = Normalize(subject);
            if (!db.Subjects.Any(item => item.Name.ToLower() == normalizedSubject))
            {
                db.Subjects.Add(new SubjectEntity
                {
                    Name = subject,
                    CreatedAt = now
                });
            }
        }

        if (!db.Users.Any())
        {
            db.Users.AddRange(
                new UserEntity { Username = "admin", Password = "123", DisplayName = "Admin", Role = "Admin", IsDepartmentHead = false, ManagedDepartment = string.Empty, CreatedAt = now },
                new UserEntity { Username = "gv_prn", Password = "123", DisplayName = "Nguyen Van A", Role = "Teacher", IsDepartmentHead = true, ManagedDepartment = "PRN222", CreatedAt = now },
                new UserEntity { Username = "gv_ai", Password = "123", DisplayName = "Le Van C", Role = "Teacher", IsDepartmentHead = true, ManagedDepartment = "AI", CreatedAt = now },
                new UserEntity { Username = "gv_thuong", Password = "123", DisplayName = "Tran Thi B", Role = "Teacher", IsDepartmentHead = false, ManagedDepartment = "PRN222", CreatedAt = now },
                new UserEntity { Username = "hocsinh", Password = "123", DisplayName = "Sinh vien Demo", Role = "Student", IsDepartmentHead = false, ManagedDepartment = string.Empty, CreatedAt = now });
        }

        db.SaveChanges();

        if (!db.TeacherSubjects.Any())
        {
            db.TeacherSubjects.AddRange(
                new TeacherSubjectEntity { Username = "gv_prn", Subject = "Razor Pages", AssignedAt = now },
                new TeacherSubjectEntity { Username = "gv_ai", Subject = "RAG", AssignedAt = now },
                new TeacherSubjectEntity { Username = "gv_thuong", Subject = "SignalR", AssignedAt = now });
            db.SaveChanges();
        }
    }

    private static DemoUser ToModel(UserEntity user)
    {
        return new DemoUser(
            user.Username,
            user.Password,
            user.DisplayName,
            user.Role,
            user.IsDepartmentHead,
            user.ManagedDepartment);
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
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
