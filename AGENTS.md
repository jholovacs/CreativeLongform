# Agent architecture

The **orchestrator agent** is the center of CreativeLongform. Every path that refines scene prose — pipeline post-draft edit and **Correct With LLM** — runs the same `AgenticEditLoop` with standardized session options from `AgentSessionFactory`.

The agent **decides**, **delegates** to role-specific models, **verifies** assertions (never blindly trusting critics), and **uses tools** to realize the author's vision.

---

## Model roles

| Role | Ollama preference | Agent surface | Responsibility |
|------|-------------------|---------------|----------------|
| **Orchestrator** | `AgentModel` | JSON tool loop | Plan, choose tools, verify, finish |
| **Writer** | `WriterModel` | `invoke_writer` | Creative rewrite of a paragraph span |
| **Editor** | `EditorModel` | `invoke_editor` | Tense, POV, formatting touch-ups |
| **Corrector** | `CorrectionModel` (fallback: Critic) | `invoke_corrector` | Grammar, spelling, punctuation |
| **Compliance critic** | `CriticModel` | `run_compliance_check` | Scene instructions, canon, voice, grammar |
| **Quality critic** | `QualityCriticModel` (fallback: Critic) | `run_quality_check` | Prose craft score and fix suggestions |

Initial scene draft prose comes from the **Writer** model *before* the agent loop (pipeline only). After that, the agent owns refinement.

---

## Session kinds

```csharp
enum AgentSessionKind { PipelinePostDraft, AuthorCorrection }
```

| Kind | Entry point | Primary goal |
|------|-------------|--------------|
| `PipelinePostDraft` | `ExecutePipelineAsync` after `GenerateDraftAsync` | Polish draft to scene brief + book directives |
| `AuthorCorrection` | `CorrectDraftAsync` (Correct With LLM) | Implement author's correction instruction |

Both kinds use the same verification policy when `Ollama:QualityGateEnabled` is true (and the run does not set `SkipQualityGate`).

**Remediation pass:** When terminal compliance or quality fails after the first agent session (`Ollama:AgenticRepassEnabled`), a second session runs with a `REMEDIATION PASS` mission listing outstanding failures.

---

## Tool catalog

Defined in `AgentToolRegistry` and the agent system prompt (`AgenticEditLoop.cs`).

| Category | Tools |
|----------|-------|
| **Context** | `read_section`, `query_lore`, `query_timeline`, `check_scene_brief`, `check_word_budget`, `find_text` |
| **Direct edit** | `replace_text`, `swap_text`, `patch_text`, `propose_patch`, `run_script`, `break_up_scene` |
| **Delegation** | `invoke_writer`, `invoke_editor`, `invoke_corrector` |
| **Verification** | `run_compliance_check`, `run_quality_check` |
| **Control** | `finish` |

Paragraph indices are **0-based**, inclusive ranges, on the **current** draft (double-newline separated — same as the web editor).

### Workflow rules

1. **Turn 1 is planning only** — `read_section`, `find_text`, `query_*`, `check_scene_brief`, `check_word_budget`, or `run_*_check`.
2. **`read_section` is mandatory** before any edit on a ¶ range (except steps inside `run_script` after the parent turn read).
3. **`finish` auto-runs** final compliance/quality checks on the current draft if stale — no escape hatch without a passing check.
4. **Delegated edits** return `edit_diff` + `delegation_verification` for the orchestrator to review.
5. **Word budget** — When the draft is below `MinWordsTarget`, use `check_word_budget` then `break_up_scene` (after `read_section` on the full draft). Beats use `expand` (rewrite ¶ range) or `insert_after` (new ¶ after anchor); each beat delegates to the Writer with a per-beat `targetWords`. Cap: `MaxSceneBreakUps` (default 3) per session, up to 8 beats per call.

---

## Verification pipeline

The agent must verify; the server enforces grounding.

```
Critic LLM (raw JSON)
    → ComplianceVerdictGrounding (drop prompt echoes, unquoted items, phantom quotes)
    → AgentDeterministicGuards (opening recitation, end notes, tense sample)
    → LanguageContextShiftDetector
    → AgentFixInstructionPrioritizer (severity ordering)
    → Agent tool result OR terminal gate + DB persistence
```

Implemented in `AgentVerification.ProcessCompliance` / `ProcessQuality`.

**Finish gates** (`TryFinishAsync`):

1. Auto or explicit `run_compliance_check` → `pass: true` on current draft hash.
2. Auto or explicit `run_quality_check` → score ≥ review threshold and no remaining `fixInstructions` (when required).
3. Author correction: agent judges mission complete (stated in `finish.reason`).

**Agent rule:** Before applying a critic `fixInstruction`, `find_text` the quoted phrase. Skip items with no draft match (hallucination).

---

## Orchestrator context (every turn)

The agent user message includes:

- Continuity brief + state-before JSON
- Authorized cast block
- Book directives
- Open compliance/quality failures
- Numbered draft reference (summarized when long)
- Recent tool history

Delegates receive compliance + quality context, default **2 ¶** of surrounding context, and optional `focusExcerpt`.

---

## Code layout

```
api/CreativeLongform.Application/
  Agent/
    AgentSessionKind.cs
    AgentSessionFactory.cs      — standardized AgentEditRunOptions + scaled budgets
    AgentSessionBudget.cs       — turn/check scaling by ¶ count
    AgentBookContextLoader.cs
    AgentVerification.cs
    AgentDeterministicGuards.cs
    AgentFixInstructionPrioritizer.cs
    AgentSceneBriefChecker.cs   — check_scene_brief
    AgentEditDiff.cs
    AgentDelegationVerifier.cs
    AgentSessionMetrics.cs
    AgentRoles.cs
    AgentWordBudget.cs        — check_word_budget + break_up_scene planning
  Services/
    AgenticEditLoop.cs
    AgenticEditLoop.TryMethods.cs
    AgentEditToolSteps.cs
    AgentEditProgress.cs / AgentEditNarrative.cs
    WorkingDocumentNotifier.cs
  Generation/
    AgentEditRunOptions.cs
    AgentToolRegistry.cs
    ...
  Services/GenerationOrchestrator.cs
```

---

## Configuration (`Ollama` section)

| Key | Purpose |
|-----|---------|
| `AgenticEditEnabled` | Run agent after initial draft |
| `AgenticEditMaxTurns` | Base max turns (scaled by ¶ count via `AgentSessionBudget`) |
| `AgenticEditMaxComplianceChecks` | Base cap on compliance checks (scaled) |
| `AgenticEditMaxQualityChecks` | Base cap on quality checks (scaled) |
| `AgenticRepassEnabled` | Second agent session when terminal gates fail |
| `QualityGateEnabled` | Wire in-loop quality + require before finish |
| `QualityCriticModel` | Optional separate quality critic (else uses `CriticModel`) |

Model assignments: **Ollama models** page / `OllamaModelPreferencesService`.

---

## UI / progress

SignalR events on `/hubs/generation`:

- `AgentEditStatus` — thinking, reflection (`conclusion` / `nextStep`), applying edits, final verification
- `AgentEditAction`, `AgentEditResult`, `AgentEditTurn`
- `WorkingDocumentUpdated` — live draft text + revision

The event log narrates every tool: planning reads, beat checklist, delegated model responses, diffs, compliance/quality results, remediation passes.

---

## Adding a new agent capability

1. Add tool to `AgentToolRegistry` (validation + summary).
2. Implement handler in `AgentEditToolSteps` / `AgenticEditLoop.TryMethods`.
3. Document in agent system prompt (`AgenticEditLoop.cs`).
4. If it calls an LLM, add a delegate on `AgentSessionDelegates` and wire in `BuildAgentDelegates`.
5. Add tests under `CreativeLongform.Application.Tests`.

Keep orchestrator prompts and agent tools in sync — **the agent system prompt is the behavioral spec**.
