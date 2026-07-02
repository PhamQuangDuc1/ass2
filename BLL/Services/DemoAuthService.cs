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

    public IReadOnlyList<SubjectInfo> SubjectCatalog
    {
        get
        {
            lock (_lock)
            {
                using var db = CreateReadyDbContext();
                return db.Subjects
                    .AsNoTracking()
                    .OrderBy(subject => subject.Department)
                    .ThenBy(subject => subject.Name)
                    .Select(subject => new SubjectInfo(subject.Name, subject.Department))
                    .ToList();
            }
        }
    }

    public IReadOnlyList<string> Departments
    {
        get
        {
            lock (_lock)
            {
                using var db = CreateReadyDbContext();
                return db.Subjects
                    .AsNoTracking()
                    .Select(subject => subject.Department)
                    .Concat(db.Users.Select(user => user.ManagedDepartment))
                    .Where(value => value != null && value != string.Empty)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToList();
            }
        }
    }

    public string? GetDepartmentHead(string? department)
    {
        department = department?.Trim() ?? string.Empty;
        var normalizedDepartment = Normalize(department);

        if (string.IsNullOrWhiteSpace(department))
        {
            return null;
        }

        lock (_lock)
        {
            using var db = CreateReadyDbContext();
            return db.Users
                .AsNoTracking()
                .Where(user => user.Role == "Teacher"
                    && user.IsDepartmentHead
                    && user.ManagedDepartment != null
                    && user.ManagedDepartment.ToLower() == normalizedDepartment)
                .OrderBy(user => user.Username)
                .Select(user => user.Username)
                .FirstOrDefault();
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
        username = username?.Trim() ?? string.Empty;
        role = role?.Trim() ?? string.Empty;
        managedDepartment = managedDepartment?.Trim() ?? string.Empty;
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
            user.ManagedDepartment = role == "Teacher" ? managedDepartment : string.Empty;

            if (role != "Teacher")
            {
                var assignments = db.TeacherSubjects.Where(item => item.Username.ToLower() == normalizedUsername).ToList();
                db.TeacherSubjects.RemoveRange(assignments);
            }

            db.SaveChanges();
            return true;
        }
    }

    public bool CreateSubject(string? subject, string? department)
    {
        subject = subject?.Trim() ?? string.Empty;
        department = department?.Trim() ?? string.Empty;
        var normalizedSubject = Normalize(subject);

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(department))
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
                Department = department,
                CreatedAt = DateTimeOffset.Now
            });
            db.SaveChanges();
            return true;
        }
    }

    public string GetSubjectDepartment(string? subject)
    {
        subject = subject?.Trim() ?? string.Empty;
        var normalizedSubject = Normalize(subject);

        if (string.IsNullOrWhiteSpace(subject))
        {
            return string.Empty;
        }

        lock (_lock)
        {
            using var db = CreateReadyDbContext();
            return db.Subjects
                .AsNoTracking()
                .Where(item => item.Name.ToLower() == normalizedSubject)
                .Select(item => item.Department)
                .FirstOrDefault() ?? string.Empty;
        }
    }

    public IReadOnlyList<string> GetSubjectsByDepartment(string? department)
    {
        department = department?.Trim() ?? string.Empty;
        var normalizedDepartment = Normalize(department);

        lock (_lock)
        {
            using var db = CreateReadyDbContext();
            return db.Subjects
                .AsNoTracking()
                .Where(item => item.Department.ToLower() == normalizedDepartment)
                .OrderBy(item => item.Name)
                .Select(item => item.Name)
                .ToList();
        }
    }

    public string? GetSubjectOwner(string? subject)
    {
        return GetSubjectOwners(subject).FirstOrDefault();
    }

    public IReadOnlyList<string> GetSubjectOwners(string? subject)
    {
        subject = subject?.Trim() ?? string.Empty;
        var normalizedSubject = Normalize(subject);

        if (string.IsNullOrWhiteSpace(subject))
        {
            return [];
        }

        lock (_lock)
        {
            using var db = CreateReadyDbContext();
            return db.TeacherSubjects
                .AsNoTracking()
                .Where(item => item.Subject.ToLower() == normalizedSubject)
                .OrderBy(item => item.Username)
                .Select(item => item.Username)
                .ToList();
        }
    }

    public bool IsSubjectAssignedToOtherTeacher(string username, string? subject)
    {
        username = username.Trim();
        return GetSubjectOwners(subject)
            .Any(owner => !string.Equals(owner, username, StringComparison.OrdinalIgnoreCase));
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
        return AssignTeacherSubjects(username, string.IsNullOrWhiteSpace(subject) ? [] : [subject.Trim()]);
    }

    public bool AssignTeacherSubjects(string username, IEnumerable<string> subjects)
    {
        username = username.Trim();
        var normalizedUsername = Normalize(username);
        var selectedSubjects = subjects
            .Select(subject => subject.Trim())
            .Where(subject => !string.IsNullOrWhiteSpace(subject))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (_lock)
        {
            using var db = CreateReadyDbContext();
            var user = db.Users.FirstOrDefault(item => item.Username.ToLower() == normalizedUsername);
            if (user is null || user.Role != "Teacher")
            {
                return false;
            }

            var catalog = db.Subjects
                .ToDictionary(subject => subject.Name, subject => subject.Department, StringComparer.OrdinalIgnoreCase);

            foreach (var subject in selectedSubjects)
            {
                if (!catalog.ContainsKey(subject))
                {
                    return false;
                }
            }

            var existingAssignments = db.TeacherSubjects
                .Where(item => item.Username.ToLower() == normalizedUsername)
                .ToList();

            foreach (var assignment in existingAssignments.Where(item => !selectedSubjects.Contains(item.Subject, StringComparer.OrdinalIgnoreCase)))
            {
                db.TeacherSubjects.Remove(assignment);
            }

            foreach (var subject in selectedSubjects)
            {
                var existing = existingAssignments.FirstOrDefault(item => string.Equals(item.Subject, subject, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    db.TeacherSubjects.Add(new TeacherSubjectEntity
                    {
                        Username = username,
                        Subject = subject,
                        Department = catalog[subject],
                        AssignedAt = DateTimeOffset.Now
                    });
                }
                else
                {
                    existing.Department = catalog[subject];
                    existing.AssignedAt = DateTimeOffset.Now;
                }
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
                    [Department] nvarchar(120) NOT NULL CONSTRAINT [DF_Subjects_Department] DEFAULT N'',
                    [CreatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_Subjects] PRIMARY KEY ([Name])
                );
            END

            IF COL_LENGTH(N'[dbo].[Subjects]', N'Department') IS NULL
                ALTER TABLE [dbo].[Subjects] ADD [Department] nvarchar(120) NOT NULL CONSTRAINT [DF_Subjects_Department] DEFAULT N'';

            IF OBJECT_ID(N'[dbo].[TeacherSubjects]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[TeacherSubjects] (
                    [Username] nvarchar(120) NOT NULL,
                    [Subject] nvarchar(120) NOT NULL,
                    [Department] nvarchar(120) NOT NULL CONSTRAINT [DF_TeacherSubjects_Department] DEFAULT N'',
                    [AssignedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_TeacherSubjects] PRIMARY KEY ([Username], [Subject])
                );

                CREATE INDEX [IX_TeacherSubjects_Username] ON [dbo].[TeacherSubjects] ([Username]);
                CREATE INDEX [IX_TeacherSubjects_Subject] ON [dbo].[TeacherSubjects] ([Subject]);
            END

            IF COL_LENGTH(N'[dbo].[TeacherSubjects]', N'Department') IS NULL
                ALTER TABLE [dbo].[TeacherSubjects] ADD [Department] nvarchar(120) NOT NULL CONSTRAINT [DF_TeacherSubjects_Department] DEFAULT N'';

            DECLARE @pkName sysname;
            SELECT @pkName = kc.name
            FROM sys.key_constraints kc
            WHERE kc.parent_object_id = OBJECT_ID(N'[dbo].[TeacherSubjects]')
              AND kc.[type] = 'PK';

            IF @pkName IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1
                    FROM sys.index_columns ic
                    JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                    WHERE ic.object_id = OBJECT_ID(N'[dbo].[TeacherSubjects]')
                      AND ic.index_id = (SELECT unique_index_id FROM sys.key_constraints WHERE name = @pkName)
                      AND c.name = N'Username'
               )
            BEGIN
                EXEC(N'ALTER TABLE [dbo].[TeacherSubjects] DROP CONSTRAINT [' + @pkName + N']');
            END

            IF NOT EXISTS (
                SELECT 1
                FROM sys.key_constraints
                WHERE parent_object_id = OBJECT_ID(N'[dbo].[TeacherSubjects]')
                  AND [type] = 'PK'
            )
            BEGIN
                ALTER TABLE [dbo].[TeacherSubjects] ADD CONSTRAINT [PK_TeacherSubjects] PRIMARY KEY ([Username], [Subject]);
            END

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TeacherSubjects_Subject' AND object_id = OBJECT_ID(N'[dbo].[TeacherSubjects]'))
                CREATE INDEX [IX_TeacherSubjects_Subject] ON [dbo].[TeacherSubjects] ([Subject]);

            ;WITH RankedDepartmentHeads AS (
                SELECT
                    [Username],
                    ROW_NUMBER() OVER (
                        PARTITION BY [ManagedDepartment]
                        ORDER BY [CreatedAt], [Username]
                    ) AS [Rank]
                FROM [dbo].[Users]
                WHERE [Role] = N'Teacher'
                  AND [IsDepartmentHead] = 1
                  AND [ManagedDepartment] <> N''
            )
            UPDATE users
            SET [IsDepartmentHead] = 0,
                [ManagedDepartment] = N''
            FROM [dbo].[Users] users
            INNER JOIN RankedDepartmentHeads ranked ON ranked.[Username] = users.[Username]
            WHERE ranked.[Rank] > 1;

            UPDATE [dbo].[Users]
            SET [ManagedDepartment] = N''
            WHERE [ManagedDepartment] IS NULL
               OR (([Role] <> N'Teacher' OR [IsDepartmentHead] = 0)
                  AND [ManagedDepartment] <> N'');
            """);

        SeedDefaultData(db);
        _schemaReady = true;
    }

    private static void SeedDefaultData(AppDbContext db)
    {
        var now = DateTimeOffset.Now;
        var defaultSubjects = new[]
        {
            new SubjectInfo("Razor Pages", "PRN222"),
            new SubjectInfo("SignalR", "PRN222"),
            new SubjectInfo("RAG", "AI")
        };

        foreach (var subject in defaultSubjects)
        {
            var normalizedSubject = Normalize(subject.Name);
            var existing = db.Subjects.FirstOrDefault(item => item.Name.ToLower() == normalizedSubject);
            if (existing is null)
            {
                db.Subjects.Add(new SubjectEntity
                {
                    Name = subject.Name,
                    Department = subject.Department,
                    CreatedAt = now
                });
            }
            else if (string.IsNullOrWhiteSpace(existing.Department))
            {
                existing.Department = subject.Department;
            }
        }

        if (!db.Users.Any())
        {
            db.Users.AddRange(
                new UserEntity { Username = "admin", Password = "123", DisplayName = "Admin", Role = "Admin", IsDepartmentHead = false, ManagedDepartment = string.Empty, CreatedAt = now },
                new UserEntity { Username = "gv_prn", Password = "123", DisplayName = "Nguyen Van A", Role = "Teacher", IsDepartmentHead = true, ManagedDepartment = "PRN222", CreatedAt = now },
                new UserEntity { Username = "gv_ai", Password = "123", DisplayName = "Le Van C", Role = "Teacher", IsDepartmentHead = true, ManagedDepartment = "AI", CreatedAt = now },
                new UserEntity { Username = "gv_thuong", Password = "123", DisplayName = "Tran Thi B", Role = "Teacher", IsDepartmentHead = false, ManagedDepartment = string.Empty, CreatedAt = now },
                new UserEntity { Username = "hocsinh", Password = "123", DisplayName = "Sinh vien Demo", Role = "Student", IsDepartmentHead = false, ManagedDepartment = string.Empty, CreatedAt = now });
        }

        db.SaveChanges();

        if (!db.TeacherSubjects.Any())
        {
            db.TeacherSubjects.AddRange(
                new TeacherSubjectEntity { Username = "gv_prn", Subject = "Razor Pages", Department = "PRN222", AssignedAt = now },
                new TeacherSubjectEntity { Username = "gv_prn", Subject = "SignalR", Department = "PRN222", AssignedAt = now },
                new TeacherSubjectEntity { Username = "gv_ai", Subject = "RAG", Department = "AI", AssignedAt = now },
                new TeacherSubjectEntity { Username = "gv_thuong", Subject = "Razor Pages", Department = "PRN222", AssignedAt = now },
                new TeacherSubjectEntity { Username = "gv_thuong", Subject = "SignalR", Department = "PRN222", AssignedAt = now });
            db.SaveChanges();
        }

        foreach (var assignment in db.TeacherSubjects.Where(item => item.Department == string.Empty))
        {
            assignment.Department = db.Subjects
                .Where(subject => subject.Name == assignment.Subject)
                .Select(subject => subject.Department)
                .FirstOrDefault() ?? string.Empty;
        }

        db.SaveChanges();
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

public sealed record SubjectInfo(string Name, string Department);

public enum RegisterResult
{
    Success,
    Invalid,
    DuplicateUsername
}
