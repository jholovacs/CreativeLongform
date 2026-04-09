namespace CreativeLongform.Domain.Entities;

public class Chapter
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public Book Book { get; set; } = null!;
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Author marks the chapter finished when all scenes are approved.</summary>
    public bool IsComplete { get; set; }
    public ICollection<Scene> Scenes { get; set; } = new List<Scene>();
}
