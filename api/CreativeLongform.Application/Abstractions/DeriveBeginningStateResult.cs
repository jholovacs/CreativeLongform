namespace CreativeLongform.Application.Abstractions;

/// <summary>Result of LLM derivation of a scene's beginning narrative state JSON.</summary>
public sealed record DeriveBeginningStateResult(string BeginningStateJson, bool DerivedFromPreviousScene);
