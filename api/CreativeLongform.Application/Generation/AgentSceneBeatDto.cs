namespace CreativeLongform.Application.Generation;

/// <summary>One beat in a <c>break_up_scene</c> expansion plan.</summary>
public sealed class AgentSceneBeatDto
{
    /// <summary>expand — rewrite/expand ¶range; insert_after — new ¶ after afterParagraph.</summary>
    public string Mode { get; set; } = "expand";

    public int? ParagraphStart { get; set; }
    public int? ParagraphEnd { get; set; }

    /// <summary>For insert_after: anchor ¶ (new content inserted after this index).</summary>
    public int? AfterParagraph { get; set; }

    public string Instruction { get; set; } = "";
    public int? TargetWords { get; set; }
}
