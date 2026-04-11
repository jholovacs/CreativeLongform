/**
 * Native `title` tooltips for the Ollama models page. Keep strings concise; browsers truncate long titles.
 */
export const OLLAMA_MODELS_FIELD_HELP = {
  backToStories: 'Return to the story list.',
  writer:
    'Model for scene prose: draft, expansion passes, and repair rewrites. Often a strong instruction-tuned or creative model.',
  critic:
    'Model for analysis: compliance, quality scoring, transition continuity checks. Can be a smaller or faster model than the writer.',
  agent:
    'Model for the agentic JSON tool loop after the first draft (read sections, apply patches). Often benefits from strong JSON adherence.',
  worldBuilding:
    'Model for book/world features: glossary, canon links, scene synopsis suggestions, and related structured outputs.',
  preState:
    'Model for inferring beginning narrative state JSON when you do not supply it and there is no prior scene to copy from. JSON-only; can differ from the prose writer.',
  postState:
    'Model for deriving end-of-scene narrative state JSON from the draft (and on finalize/correct when post-state is recomputed). JSON-only.',

  pullModel:
    'Library tag to pull with ollama pull on the server (e.g. llama3.2, mistral). Large downloads can take many minutes.',
  importUrl: 'Direct HTTPS link to a .gguf file. The API downloads it into staging and registers it with ollama create.',
  importName:
    'Name to register in Ollama (what you pass to the API and see in ollama list). Use letters, numbers, dashes; no spaces.',

  tagSetWriter: 'Set this installed tag as the Writer model (does not save until you click Save assignments).',
  tagPickWriter: 'Writer slot',
  tagPickCritic: 'Critic slot',
  tagPickAgent: 'Agent slot',
  tagPickWorldBuilding: 'World-building slot',
  tagPickPreState: 'Pre-state slot',
  tagPickPostState: 'Post-state slot',

  saveAssignments:
    'Persist all model assignment fields to the database. Empty fields clear the DB override for that role (falls back to appsettings / env).',
  useDefaultWriter: 'Remove DB override for Writer; use Ollama:WriterModel / environment default.',
  useDefaultCritic: 'Remove DB override for Critic; use Ollama:CriticModel default.',
  useDefaultAgent: 'Remove DB override for Agent; use Ollama:AgentModel or Writer.',
  useDefaultWorldBuilding: 'Remove DB override for World-building; use Ollama:WorldBuildingModel or Writer.',
  useDefaultPreState: 'Remove DB override for Pre-state; use Ollama:PreStateModel or Writer.',
  useDefaultPostState: 'Remove DB override for Post-state; use Ollama:PostStateModel or Writer.',

  pullButton:
    'Download the model from the Ollama library into the server’s Ollama store (same as ollama pull). Progress streams from the server; the bar shows layer byte progress when Ollama reports totals.',
  deleteFromDisk:
    'Delete this model from the Ollama host disk to free space (ollama rm). Does not change saved assignments—clear slots if the name is still listed.',
  installedSizeDisk: 'Total size of model files on disk (Ollama GET /api/tags size).',
  installedSizeVram:
    'VRAM reported by Ollama for this model while it is loaded (GET /api/ps size_vram). Shown only when the model is in memory.',
  diskSpaceBanner:
    'Free/total space on the API host filesystem that contains the configured path (Ollama:DiskSpaceCheckPath, or ImportStagingDirectory when the former is empty). May differ from the Ollama-only volume unless you mount the same path.',
  importButton: 'Download the GGUF from the URL, then ollama create with the given name. Requires ImportStagingDirectory and shared volume in Docker.',

  changelogSearch: 'Search across timestamp, role, previous/new model names, and source (case-insensitive).',
  changelogRoleFilter: 'Show only changes for one pipeline role.',
  changelogSourceFilter: 'Keep rows whose source string contains this text (e.g. ui).'
} as const;
