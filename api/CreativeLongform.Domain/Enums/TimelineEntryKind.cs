namespace CreativeLongform.Domain.Enums;

/// <summary>What a <see cref="Entities.TimelineEntry"/> represents on the story timeline.</summary>
public enum TimelineEntryKind
{
    /// <summary>A scene in the manuscript; ties to <see cref="Entities.Scene"/>.</summary>
    Scene = 0,

    /// <summary>A story-significant beat that is not a scene (e.g. lore event, off-page turning point).</summary>
    WorldEvent = 1
}
