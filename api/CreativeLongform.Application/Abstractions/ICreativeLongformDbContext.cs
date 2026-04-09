using CreativeLongform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Application.Abstractions;

public interface ICreativeLongformDbContext
{
    DbSet<Book> Books { get; }
    DbSet<Chapter> Chapters { get; }
    DbSet<Scene> Scenes { get; }
    DbSet<GenerationRun> GenerationRuns { get; }
    DbSet<StateSnapshot> StateSnapshots { get; }
    DbSet<LlmCall> LlmCalls { get; }
    DbSet<ComplianceEvaluation> ComplianceEvaluations { get; }
    DbSet<WorldElement> WorldElements { get; }
    DbSet<WorldElementLink> WorldElementLinks { get; }
    DbSet<SceneWorldElement> SceneWorldElements { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
