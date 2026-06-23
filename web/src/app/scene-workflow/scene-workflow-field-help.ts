/**
 * Native `title` tooltips for Scene Workflow fields. Keep strings concise; browsers truncate long titles.
 */
export const SCENE_WORKFLOW_FIELD_HELP = {
  reload: 'Reload books, chapters, and scenes from the server without leaving the page.',

  bookSelect: 'Which story this scene belongs to. Example: The Glass Meridian.',
  chapterSelect: 'Chapter that contains this scene. Example: Part II — The Harbor.',
  sceneSelect: 'The scene you are drafting. Order is the scene index within the chapter.',
  chapterComplete: 'Mark the chapter finished when all its scenes are done and you are satisfied.',
  manuscriptPanel:
    'Finalized prose for this scene only. It is not replaced when you run Generate again; use the Draft workspace for the new run.',

  clearSceneManuscript:
    'Remove finalized manuscript and approved end-state for this scene so you can generate and finalize a new draft.',

  llmThinkingNotesPanel:
    'Reasoning the model returned in thinking blocks. This is not part of your manuscript — stored here for reference only.',

  synopsis:
    'Main beat and purpose of this scene for generation. Example: Mara confronts her brother at the docks about the forged letter.',
  instructions:
    'Extra constraints: tone, beats to hit, what to avoid. Example: Keep subtext; no exposition about the treaty.',
  expectedEnd:
    'Optional: where the scene should land emotionally or plot-wise. Example: She leaves without an answer.',

  narrativePerspective:
    'Point of view for this scene. Example: third limited (Mara), or first person.',
  narrativeTense:
    'Verb tense for the prose. Example: past, present. Defaults from earlier scenes when left blank on open.',

  beginningStateJson:
    'JSON continuity snapshot at scene start. Defaults from the previous scene’s end-state when this scene has none saved. Generation uses the saved value.',

  beginningStateProse:
    'Describe who is present, where they are, mood, and other facts at scene open in plain language. Use Convert to JSON to produce the schema snapshot used for generation.',

  convertBeginningStateFromProse:
    'Run the pre-state model to translate your prose description into schema-compliant beginning-state JSON. Uses your prose and prior-scene continuity only — not the scene synopsis.',

  deriveBeginningState:
    'Re-infer beginning state with the LLM. With a prior scene: merge that scene’s beginning state through its manuscript into this scene’s entry state. Without a prior scene: work backward from this scene’s synopsis (and draft/manuscript if present) to infer state at open.',

  worldElementsSearch:
    'Server-side filter: title, kind, summary, or detail (case-insensitive). Example: harbor or Character.',
  modalWorldSearch:
    'Server-side filter for this list: title, kind, summary, or detail (case-insensitive).',

  correctInstruction:
    'Tell the model what to change. With text selected in the draft, only that passage is rewritten; the full draft and scene/world context are still sent. With no selection, the whole draft is revised.',

  suggestModalClose: 'Close without applying changes.',

  suggestImprovements:
    'Runs a separate LLM pass that lists optional paragraph-level suggestions. Each item shows current text (with one paragraph of context before and after when available) and what would change. Nothing is applied until you click Apply or use an instruction with Correct.',

  selectInDraft:
    'Focus the draft and select the paragraph range this suggestion refers to (same span as Apply replacement).',

  cancelGeneration:
    'Stop this draft run after the current LLM step. Use if the model is going in the wrong direction; you can generate again.',

  qualityAcceptMinScore:
    'Critic score 0–100. At or above this, the pipeline accepts the draft without an automated quality repair pass. Default 75.',
  qualityReviewOnlyMinScore:
    'Minimum score to pass. Between this and “no repair” above, the run still passes but issues are listed for your manual review. Default 55.',

  draftTargetMinWords:
    'Minimum word count for the scene draft. If the model writes fewer words, an expansion pass tries to reach at least this length.',
  draftTargetMaxWords:
    'Upper end of the target band shown to the writer model (e.g. “roughly min–max words”). Should be ≥ min; the server will align them if reversed.',

  copyPrompt: 'Copy the full request payload (prompt) sent to the model to the clipboard.',

  copyLlmResponse: 'Copy the full LLM response text to the clipboard.'
};
