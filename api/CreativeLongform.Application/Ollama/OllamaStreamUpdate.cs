namespace CreativeLongform.Application.Ollama;

/// <summary>Incremental token from an Ollama streaming chat response.</summary>
public sealed record OllamaStreamUpdate(string Delta, string ContentSoFar, bool Done);
