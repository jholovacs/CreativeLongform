namespace CreativeLongform.Domain.Entities;

public class Chapter
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public Book Book { get; set; } = null!;
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public ICollection<Scene> Scenes { get; set; } = new List<Scene>();
}
