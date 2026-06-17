using Microsoft.EntityFrameworkCore;

namespace ass2.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
    public DbSet<ChatTurnEntity> ChatTurns => Set<ChatTurnEntity>();
    public DbSet<SourceMatchEntity> SourceMatches => Set<SourceMatchEntity>();

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
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
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
}
