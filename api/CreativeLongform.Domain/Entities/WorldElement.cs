using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Domain.Entities;

public class WorldElement
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public Book Book { get; set; } = null!;
    public WorldElementKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public WorldElementStatus Status { get; set; }
    public WorldElementProvenance Provenance { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<SceneWorldElement> SceneWorldElements { get; set; } = new List<SceneWorldElement>();
    public ICollection<WorldElementLink> OutgoingLinks { get; set; } = new List<WorldElementLink>();
    public ICollection<WorldElementLink> IncomingLinks { get; set; } = new List<WorldElementLink>();
}
