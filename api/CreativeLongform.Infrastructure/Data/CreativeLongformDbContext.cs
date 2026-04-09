using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Infrastructure.Data;

public sealed class CreativeLongformDbContext : DbContext, ICreativeLongformDbContext
{
    public CreativeLongformDbContext(DbContextOptions<CreativeLongformDbContext> options)
        : base(options)
    {
    }

    public DbSet<Book> Books => Set<Book>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<GenerationRun> GenerationRuns => Set<GenerationRun>();
    public DbSet<StateSnapshot> StateSnapshots => Set<StateSnapshot>();
    public DbSet<LlmCall> LlmCalls => Set<LlmCall>();
    public DbSet<ComplianceEvaluation> ComplianceEvaluations => Set<ComplianceEvaluation>();
    public DbSet<WorldElement> WorldElements => Set<WorldElement>();
    public DbSet<WorldElementLink> WorldElementLinks => Set<WorldElementLink>();
    public DbSet<SceneWorldElement> SceneWorldElements => Set<SceneWorldElement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Book>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(512);
            e.Property(x => x.StoryToneAndStyle).HasMaxLength(2000);
            e.Property(x => x.ContentStyleNotes).HasMaxLength(4000);
            e.Property(x => x.MeasurementSystemJson).HasColumnType("jsonb");
            e.HasIndex(x => x.Title);
        });

        modelBuilder.Entity<Chapter>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(512);
            e.HasOne(x => x.Book).WithMany(x => x.Chapters).HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BookId, x.Order }).IsUnique();
        });

        modelBuilder.Entity<Scene>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(512);
            e.Property(x => x.Instructions).HasMaxLength(16000);
            e.Property(x => x.ExpectedEndStateNotes).HasMaxLength(8000);
            e.Property(x => x.LatestDraftText).HasColumnType("text");
            e.HasOne(x => x.Chapter).WithMany(x => x.Scenes).HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ChapterId, x.Order }).IsUnique();
        });

        modelBuilder.Entity<GenerationRun>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.IdempotencyKey).HasMaxLength(128);
            e.Property(x => x.FailureReason).HasMaxLength(8000);
            e.Property(x => x.FinalDraftText).HasColumnType("text");
            e.HasOne(x => x.Scene).WithMany(x => x.GenerationRuns).HasForeignKey(x => x.SceneId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SceneId, x.IdempotencyKey });
        });

        modelBuilder.Entity<StateSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.StateJson).HasColumnType("jsonb");
            e.HasOne(x => x.GenerationRun).WithMany(x => x.StateSnapshots).HasForeignKey(x => x.GenerationRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LlmCall>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Model).HasMaxLength(256);
            e.Property(x => x.RequestJson).HasColumnType("jsonb");
            e.Property(x => x.ResponseText).HasColumnType("text");
            e.HasOne(x => x.GenerationRun).WithMany(x => x.LlmCalls).HasForeignKey(x => x.GenerationRunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Book).WithMany(x => x.WorldLlmCalls).HasForeignKey(x => x.BookId)
                .OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_LlmCall_GenerationOrBook",
                "\"GenerationRunId\" IS NOT NULL OR \"BookId\" IS NOT NULL"));
        });

        modelBuilder.Entity<WorldElement>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(512);
            e.Property(x => x.Slug).HasMaxLength(256);
            e.Property(x => x.Summary).HasMaxLength(4000);
            e.Property(x => x.Detail).HasColumnType("text");
            e.Property(x => x.MetadataJson).HasColumnType("jsonb");
            e.HasOne(x => x.Book).WithMany(x => x.WorldElements).HasForeignKey(x => x.BookId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BookId, x.Slug }).IsUnique().HasFilter("\"Slug\" IS NOT NULL");
        });

        modelBuilder.Entity<WorldElementLink>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.RelationLabel).HasMaxLength(128);
            e.HasOne(x => x.FromWorldElement).WithMany(x => x.OutgoingLinks).HasForeignKey(x => x.FromWorldElementId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ToWorldElement).WithMany(x => x.IncomingLinks).HasForeignKey(x => x.ToWorldElementId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.FromWorldElementId, x.ToWorldElementId, x.RelationLabel }).IsUnique();
        });

        modelBuilder.Entity<SceneWorldElement>(e =>
        {
            e.HasKey(x => new { x.SceneId, x.WorldElementId });
            e.HasOne(x => x.Scene).WithMany(x => x.SceneWorldElements).HasForeignKey(x => x.SceneId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.WorldElement).WithMany(x => x.SceneWorldElements).HasForeignKey(x => x.WorldElementId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ComplianceEvaluation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(64);
            e.Property(x => x.VerdictJson).HasColumnType("jsonb");
            e.HasOne(x => x.GenerationRun).WithMany(x => x.ComplianceEvaluations).HasForeignKey(x => x.GenerationRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
