namespace CreativeLongform.Domain.Entities;

/// <summary>
/// Directed relationship between two <see cref="WorldElement"/> rows in the same book (graph edge with optional semantics).
/// </summary>
public class WorldElementLink
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>FK to source element.</summary>
    public Guid FromWorldElementId { get; set; }
    /// <summary>Navigation to source element.</summary>
    public WorldElement FromWorldElement { get; set; } = null!;
    /// <summary>FK to target element.</summary>
    public Guid ToWorldElementId { get; set; }
    /// <summary>Navigation to target element.</summary>
    public WorldElement ToWorldElement { get; set; } = null!;
    /// <summary>Optional relation label, e.g. located_in, part_of, contradicts.</summary>
    public string RelationLabel { get; set; } = string.Empty;

    /// <summary>Optional nuance or constraints for this relationship (prompts only when both endpoints are scene-linked).</summary>
    public string? RelationDetail { get; set; }
}
