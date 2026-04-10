namespace CreativeLongform.Domain.Entities;

/// <summary>
/// A narrative section within a <see cref="Book"/>; owns ordered <see cref="Scene"/> rows. Used for scene workflow navigation,
/// chapter manuscript assembly, and completion tracking.
/// </summary>
public class Chapter
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>FK to parent book.</summary>
    public Guid BookId { get; set; }
    /// <summary>Navigation to parent book.</summary>
    public Book Book { get; set; } = null!;
    /// <summary>Stable ordering within the book (1-based or 0-based per UI; unique per book).</summary>
    public int Order { get; set; }
    /// <summary>Display title for lists and manuscript headings.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Author marks the chapter finished when all scenes are approved.</summary>
    public bool IsComplete { get; set; }
    /// <summary>Optional assembled or edited chapter manuscript (scene prose combined or hand-edited).</summary>
    public string? ManuscriptText { get; set; }
    /// <summary>Scenes in reading order; cascade-deleted with the chapter.</summary>
    public ICollection<Scene> Scenes { get; set; } = new List<Scene>();
}
