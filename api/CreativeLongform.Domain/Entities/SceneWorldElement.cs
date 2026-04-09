namespace CreativeLongform.Domain.Entities;

public class SceneWorldElement
{
    public Guid SceneId { get; set; }
    public Scene Scene { get; set; } = null!;
    public Guid WorldElementId { get; set; }
    public WorldElement WorldElement { get; set; } = null!;
}
