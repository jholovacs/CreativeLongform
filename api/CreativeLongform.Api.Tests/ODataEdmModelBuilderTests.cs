using CreativeLongform.Api.OData;
using Microsoft.OData.Edm;

namespace CreativeLongform.Api.Tests;

/// <summary>
/// Fast smoke tests (no database) for <see cref="ODataEdmModelBuilder"/> EDM configuration used by OData controllers.
/// </summary>
public sealed class ODataEdmModelBuilderTests
{
    /// <summary>
    /// <para><b>System under test:</b> <see cref="ODataEdmModelBuilder.Build"/>.</para>
    /// <para><b>Test case:</b> Build the EDM model and look up several entity sets by name.</para>
    /// <para><b>Expected result:</b> Model is non-null; <c>Books</c>, <c>SceneWorldElements</c>, <c>TimelineEntries</c>, <c>LlmCalls</c> exist.</para>
    /// <para><b>Why it matters:</b> Missing entity sets cause runtime failures for all OData clients; this catches renames/omissions early.</para>
    /// </summary>
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
    /// <para><b>System under test:</b> EDM mapping of <c>GenerationRun.status</c> for OData filters.</para>
    /// <para><b>Test case:</b> Resolve <c>GenerationRuns</c> entity set and inspect <c>status</c> property type.</para>
    /// <para><b>Expected result:</b> <c>status</c> is an enum type named <c>GenerationRunStatus</c> (full name contains that string).</para>
    /// <para><b>Why it matters:</b> <c>status eq 4</c> is rejected for enum properties; OData expects <c>Namespace.EnumName'MemberName'</c>. Keeps API + SPA filter strings aligned.</para>
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
