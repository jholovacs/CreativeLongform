namespace CreativeLongform.Application.Options;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434/api";
    public string WriterModel { get; set; } = "llama3.2";
    public string CriticModel { get; set; } = "llama3.2";

    /// <summary>Minimum word count for scene drafts (long-form fiction).</summary>
    public int DraftMinWords { get; set; } = 600;

    /// <summary>Ollama num_predict for draft and repair steps (raise if output truncates).</summary>
    public int DraftNumPredict { get; set; } = 8192;

    /// <summary>If the first draft is shorter than DraftMinWords, run one expansion pass.</summary>
    public bool DraftExpandIfShort { get; set; } = true;

    /// <summary>Run agentic paragraph-level edit loop after the initial draft (before post-state).</summary>
    public bool AgenticEditEnabled { get; set; } = true;

    /// <summary>Max LLM turns in the agentic edit loop (each turn is one tool JSON response).</summary>
    public int AgenticEditMaxTurns { get; set; } = 8;

    /// <summary>Ollama num_predict for each agentic edit JSON turn.</summary>
    public int AgenticEditNumPredict { get; set; } = 2048;
}
