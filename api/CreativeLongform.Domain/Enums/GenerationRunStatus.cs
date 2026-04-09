namespace CreativeLongform.Domain.Enums;

public enum GenerationRunStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    /// <summary>Draft passed gates; awaiting user review before post-state and canon.</summary>
    AwaitingUserReview = 4,

    /// <summary>User cancelled the run before completion.</summary>
    Cancelled = 5
}
