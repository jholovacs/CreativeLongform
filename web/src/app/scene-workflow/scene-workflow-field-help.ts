/**
 * Native `title` tooltips for Scene Workflow fields. Keep strings concise; browsers truncate long titles.
 */
export const SCENE_WORKFLOW_FIELD_HELP = {
  reload: 'Reload books, chapters, and scenes from the server without leaving the page.',

  bookSelect: 'Which story this scene belongs to. Example: The Glass Meridian.',
  chapterSelect: 'Chapter that contains this scene. Example: Part II — The Harbor.',
  sceneSelect: 'The scene you are drafting. Order is the scene index within the chapter.',
  chapterComplete: 'Mark the chapter finished when all its scenes are done and you are satisfied.',

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
    'JSON snapshot of story/world state at scene start. Empty uses the previous scene’s approved end-state when available. Example: {"location":"Harbor"}',

  worldElementsSearch:
    'Server-side filter: title, kind, summary, or detail (case-insensitive). Example: harbor or Character.',
  modalWorldSearch:
    'Server-side filter for this list: title, kind, summary, or detail (case-insensitive).',

  correctInstruction:
    'Tell the model what to change in the draft. Example: Tighten the café dialogue; keep the rain motif.',

  suggestModalClose: 'Close without applying changes.',

  cancelGeneration:
    'Stop this draft run after the current LLM step. Use if the model is going in the wrong direction; you can generate again.',

  copyPrompt: 'Copy the full request payload (prompt) sent to the model to the clipboard.',

  copyLlmResponse: 'Copy the LLM response text (truncated preview from the server) to the clipboard.'
};
