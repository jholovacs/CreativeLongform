using CreativeLongform.Domain.Entities;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace CreativeLongform.Api.OData;

public static class ODataEdmModelBuilder
{
    public static IEdmModel Build()
    {
        var builder = new ODataConventionModelBuilder();
        builder.EntitySet<Book>("Books");
        builder.EntitySet<Chapter>("Chapters");
        builder.EntitySet<Scene>("Scenes");
        builder.EntitySet<GenerationRun>("GenerationRuns");
        builder.EntitySet<WorldElement>("WorldElements");
        builder.EntitySet<WorldElementLink>("WorldElementLinks");
        builder.EntitySet<SceneWorldElement>("SceneWorldElements");
        builder.EntityType<SceneWorldElement>().HasKey(e => new { e.SceneId, e.WorldElementId });
        return builder.GetEdmModel();
    }
}
