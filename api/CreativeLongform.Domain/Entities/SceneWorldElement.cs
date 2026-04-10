namespace CreativeLongform.Domain.Entities;

/// <summary>
/// Many-to-many join: which <see cref="WorldElement"/> entries are in scope for a <see cref="Scene"/>&apos;s generation prompts.
/// </summary>
public class SceneWorldElement
{
    /// <summary>FK to scene (part of composite key).</summary>
    public Guid SceneId { get; set; }
    /// <summary>Navigation to scene.</summary>
    public Scene Scene { get; set; } = null!;
    /// <summary>FK to world element (part of composite key).</summary>
    public Guid WorldElementId { get; set; }
    /// <summary>Navigation to world element.</summary>
    public WorldElement WorldElement { get; set; } = null!;
}
