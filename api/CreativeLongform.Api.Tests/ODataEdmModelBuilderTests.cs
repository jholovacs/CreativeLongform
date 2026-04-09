using CreativeLongform.Api.OData;

namespace CreativeLongform.Api.Tests;

/// <summary>Fast smoke test (no database) for OData EDM configuration.</summary>
public sealed class ODataEdmModelBuilderTests
{
    [Fact]
    public void Build_returns_model_with_expected_entity_sets()
    {
        var model = ODataEdmModelBuilder.Build();
        Assert.NotNull(model);
        Assert.NotNull(model.EntityContainer.FindEntitySet("Books"));
        Assert.NotNull(model.EntityContainer.FindEntitySet("SceneWorldElements"));
    }
}
