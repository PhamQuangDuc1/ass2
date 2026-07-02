using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
    public DbSet<DocumentChunkEntity> DocumentChunks => Set<DocumentChunkEntity>();
    public DbSet<ChatTurnEntity> ChatTurns => Set<ChatTurnEntity>();
    public DbSet<SourceMatchEntity> SourceMatches => Set<SourceMatchEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<SubjectEntity> Subjects => Set<SubjectEntity>();
    public DbSet<TeacherSubjectEntity> TeacherSubjects => Set<TeacherSubjectEntity>();
    public DbSet<SystemSettingEntity> SystemSettings => Set<SystemSettingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentEntity>(entity =>
        {
            entity.ToTable("Documents");
            entity.HasKey(document => document.Id);
            entity.Property(document => document.Title).HasMaxLength(200);
            entity.Property(document => document.FileName).HasMaxLength(260);
            entity.Property(document => document.Department).HasMaxLength(100);
            entity.Property(document => document.Subject).HasMaxLength(120);
            entity.Property(document => document.Chapter).HasMaxLength(120);
            entity.Property(document => document.Teacher).HasMaxLength(120);
            entity.Property(document => document.UploadedBy).HasMaxLength(120);
            entity.Property(document => document.UploadedByUserId).HasMaxLength(120);
            entity.Property(document => document.ContentType).HasMaxLength(120);
            entity.HasMany(document => document.Chunks)
                .WithOne(chunk => chunk.Document)
                .HasForeignKey(chunk => chunk.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentChunkEntity>(entity =>
        {
            entity.ToTable("DocumentChunks");
            entity.HasKey(chunk => chunk.Id);
            entity.Property(chunk => chunk.Title).HasMaxLength(200);
            entity.Property(chunk => chunk.Department).HasMaxLength(100);
            entity.Property(chunk => chunk.Subject).HasMaxLength(120);
            entity.Property(chunk => chunk.Chapter).HasMaxLength(120);
            entity.Property(chunk => chunk.Teacher).HasMaxLength(120);
            entity.HasIndex(chunk => chunk.DocumentId);
        });

        modelBuilder.Entity<ChatTurnEntity>(entity =>
        {
            entity.ToTable("ChatTurns");
            entity.HasKey(turn => turn.Id);
            entity.Property(turn => turn.SessionId).HasMaxLength(80);
            entity.HasMany(turn => turn.Sources)
                .WithOne(source => source.ChatTurn)
                .HasForeignKey(source => source.ChatTurnId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SourceMatchEntity>(entity =>
        {
            entity.ToTable("SourceMatches");
            entity.HasKey(source => source.Id);
            entity.Property(source => source.Title).HasMaxLength(200);
            entity.Property(source => source.Subject).HasMaxLength(120);
            entity.Property(source => source.Chapter).HasMaxLength(120);
            entity.Property(source => source.Teacher).HasMaxLength(120);
            entity.Property(source => source.FileName).HasMaxLength(260);
        });

        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(user => user.Username);
            entity.Property(user => user.Username).HasMaxLength(120);
            entity.Property(user => user.Password).HasMaxLength(200);
            entity.Property(user => user.DisplayName).HasMaxLength(160);
            entity.Property(user => user.Role).HasMaxLength(40);
            entity.Property(user => user.ManagedDepartment).HasMaxLength(120);
        });

        modelBuilder.Entity<SubjectEntity>(entity =>
        {
            entity.ToTable("Subjects");
            entity.HasKey(subject => subject.Name);
            entity.Property(subject => subject.Name).HasMaxLength(120);
            entity.Property(subject => subject.Department).HasMaxLength(120);
        });

        modelBuilder.Entity<TeacherSubjectEntity>(entity =>
        {
            entity.ToTable("TeacherSubjects");
            entity.HasKey(assignment => new { assignment.Username, assignment.Subject });
            entity.Property(assignment => assignment.Username).HasMaxLength(120);
            entity.Property(assignment => assignment.Subject).HasMaxLength(120);
            entity.Property(assignment => assignment.Department).HasMaxLength(120);
            entity.HasIndex(assignment => assignment.Username);
            entity.HasIndex(assignment => assignment.Subject);
            entity.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(assignment => assignment.Username)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<SubjectEntity>()
                .WithMany()
                .HasForeignKey(assignment => assignment.Subject)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SystemSettingEntity>(entity =>
        {
            entity.ToTable("SystemSettings");
            entity.HasKey(setting => setting.Key);
            entity.Property(setting => setting.Key).HasMaxLength(120);
            entity.Property(setting => setting.Value).HasMaxLength(400);
        });
    }
}

public sealed class DocumentEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Chapter { get; set; } = string.Empty;
    public string Teacher { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public string UploadedByUserId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[]? FileContent { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public List<DocumentChunkEntity> Chunks { get; set; } = [];
}

public sealed class DocumentChunkEntity
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public DocumentEntity? Document { get; set; }
    public int ChunkIndex { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Chapter { get; set; } = string.Empty;
    public string Teacher { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Embedding { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ChatTurnEntity
{
    public Guid Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public List<SourceMatchEntity> Sources { get; set; } = [];
}

public sealed class SourceMatchEntity
{
    public Guid Id { get; set; }
    public Guid ChatTurnId { get; set; }
    public ChatTurnEntity? ChatTurn { get; set; }
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Chapter { get; set; } = string.Empty;
    public string Teacher { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public int Score { get; set; }
    public string FileName { get; set; } = string.Empty;
    public bool HasOriginalFile { get; set; }
}

public sealed class UserEntity
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "Student";
    public bool IsDepartmentHead { get; set; }
    public string ManagedDepartment { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SubjectEntity
{
    public string Name { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TeacherSubjectEntity
{
    public string Username { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateTimeOffset AssignedAt { get; set; }
}

public sealed class SystemSettingEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
