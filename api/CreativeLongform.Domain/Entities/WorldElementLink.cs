namespace CreativeLongform.Domain.Entities;

public class WorldElementLink
{
    public Guid Id { get; set; }
    public Guid FromWorldElementId { get; set; }
    public WorldElement FromWorldElement { get; set; } = null!;
    public Guid ToWorldElementId { get; set; }
    public WorldElement ToWorldElement { get; set; } = null!;
    /// <summary>Optional relation label, e.g. located_in, part_of, contradicts.</summary>
    public string RelationLabel { get; set; } = string.Empty;

    /// <summary>Optional nuance or constraints for this relationship (prompts only when both endpoints are scene-linked).</summary>
    public string? RelationDetail { get; set; }
}
