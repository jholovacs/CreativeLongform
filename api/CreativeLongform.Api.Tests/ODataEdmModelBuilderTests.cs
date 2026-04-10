using CreativeLongform.Api.OData;
using Microsoft.OData.Edm;

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
        Assert.NotNull(model.EntityContainer.FindEntitySet("TimelineEntries"));
        Assert.NotNull(model.EntityContainer.FindEntitySet("LlmCalls"));
    }

    /// <summary>
    /// <c>status eq 4</c> is rejected for enum properties; OData expects
    /// <c>Namespace.EnumName'MemberName'</c>. Keeps API + SPA filter strings aligned.
    /// </summary>
    [Fact]
    public void GenerationRun_status_property_maps_to_enum_for_odata_filters()
    {
        var model = ODataEdmModelBuilder.Build();
        var set = model.EntityContainer.FindEntitySet("GenerationRuns");
        Assert.NotNull(set);
        var statusProp = set.EntityType().FindProperty("status");
        Assert.NotNull(statusProp);
        var enumType = statusProp.Type.Definition as IEdmEnumType;
        Assert.NotNull(enumType);
        Assert.Equal("GenerationRunStatus", enumType.Name);
        Assert.Contains("GenerationRunStatus", enumType.FullName());
    }
}
