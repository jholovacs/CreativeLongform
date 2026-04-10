import { CommonModule, Location } from '@angular/common';
import { Component, ElementRef, OnDestroy, OnInit, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HubConnection } from '@microsoft/signalr';
import { Subscription } from 'rxjs';
import {
  applyParagraphReplace,
  getParagraphNeighborhood,
  getParagraphSelectionRangeInDraft
} from '../core/draft-paragraph';
import { ParsedComplianceVerdict, parseComplianceVerdictJson } from '../core/compliance-verdict-parse';
import { formatJsonPretty } from '../core/json-format';
import { Book, Chapter, ComplianceEvaluation, Scene } from '../models/entities';
import { CorrectDraftResponse, GenerationService } from '../services/generation.service';
import { ODataService } from '../services/odata.service';
import { DraftRecommendationItem, SceneWorkflowService } from '../services/scene-workflow.service';
import { LlmWorkingIndicatorComponent } from '../shared/llm-working-indicator/llm-working-indicator.component';
import { UiIconComponent } from '../shared/ui-icon.component';
import { SCENE_WORKFLOW_FIELD_HELP } from '../scene-workflow/scene-workflow-field-help';

/** Before/after preview for a suggestion card (paragraph neighborhood + problem vs replacement or instruction). */
type SuggestionDiffPreview =
  | {
      mode: 'replace';
      beforeContext: string;
      problemSpan: string;
      afterContext: string;
      replacementText: string;
    }
  | {
      mode: 'rewrite';
      beforeContext: string;
      problemSpan: string;
      afterContext: string;
      instruction: string;
    };

@Component({
  selector: 'app-scene-draft',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, UiIconComponent, LlmWorkingIndicatorComponent],
  templateUrl: './scene-draft.component.html',
  styleUrl: './scene-draft.component.scss'
})
export class SceneDraftComponent implements OnInit, OnDestroy {
  @ViewChild('draftBody') draftBody?: ElementRef<HTMLTextAreaElement>;

  readonly fieldHelp = SCENE_WORKFLOW_FIELD_HELP;

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly odata = inject(ODataService);
  private readonly generation = inject(GenerationService);
  private readonly sceneWorkflow = inject(SceneWorkflowService);

  sceneId = '';
  bookId = '';
  bookTitle = '';
  chapterTitle = '';
  sceneTitle = '';

  loading = true;
  error: string | null = null;
  busy = false;
  generationRunId: string | null = null;

  /** Persisted pipeline compliance / quality / transition rows for the active run. */
  complianceEvaluations: ComplianceEvaluation[] = [];
  /** Live SignalR notes (e.g. compliance detail) while connected to the run. */
  draftReviewNotes: Array<{ step: string; detail: string }> = [];

  draftText = '';
  correctInstruction = '';
  lastStateTableJson: string | null = null;

  draftRecommendations: DraftRecommendationItem[] = [];
  recommendationsBusy = false;
  recommendationsError: string | null = null;
  private static readonly maxDraftCharsForAnalysis = 100_000;
  private savedDraftSelectionStart = 0;
  private savedDraftSelectionEnd = 0;
  draftReviewFocused = false;
  draftScrollTop = 0;

  correctModalOpen = false;
  correctModalBusy = false;
  correctModalProposedText = '';
  correctModalEditedText = '';
  private draftTextSnapshotBeforeCorrect = '';
  private pendingPostSnapshotBeforeCorrect: string | null = null;
  private lastStateTableJsonSnapshotBeforeCorrect: string | null = null;
  /** Mirrors `Scene.pendingPostStateJson` from the last load/sync. */
  pendingPostStateRaw: string | null = null;

  endStateOpen = false;

  onEndStateToggle(ev: Event): void {
    const d = ev.target as HTMLDetailsElement | null;
    if (d?.tagName === 'DETAILS') {
      this.endStateOpen = d.open;
    }
  }

  private recommendationsSub: Subscription | undefined;
  private reviewDraftSub: Subscription | undefined;
  private complianceSub: Subscription | undefined;
  private hubConnection: HubConnection | null = null;
  private hubConnectedRunId: string | null = null;

  ngOnInit(): void {
    this.sceneId = this.route.snapshot.paramMap.get('sceneId') ?? '';
    if (!this.sceneId) {
      this.error = 'Missing scene.';
      this.loading = false;
      return;
    }
    const nav = this.location.getState() as { generationRunId?: string };
    if (nav?.generationRunId) {
      this.generationRunId = nav.generationRunId;
    }
    this.load();
  }

  ngOnDestroy(): void {
    this.recommendationsSub?.unsubscribe();
    this.reviewDraftSub?.unsubscribe();
    this.complianceSub?.unsubscribe();
    void this.disconnectComplianceHub();
  }

  private load(): void {
    this.loading = true;
    this.error = null;
    this.odata.getBooksWithScenes().subscribe({
      next: (res) => {
        const books = res.value ?? [];
        const ctx = this.findSceneContext(books, this.sceneId);
        if (!ctx) {
          this.error = 'Scene not found.';
          this.loading = false;
          return;
        }
        this.bookId = ctx.book.id;
        this.bookTitle = ctx.book.title;
        this.chapterTitle = ctx.chapter.title;
        this.sceneTitle = ctx.scene.title;
        this.syncFormFromScene(ctx.scene);
        this.loading = false;
        if (!this.generationRunId) {
          this.odata.getGenerationRunAwaitingReview(this.sceneId).subscribe({
            next: (id) => {
              this.generationRunId = id;
              if (!id && !this.draftText.trim()) {
                this.error =
                  'No draft run is awaiting review for this scene. Use Scene Workflow to generate a draft, or pick another scene.';
              }
              this.syncComplianceForRun();
            },
            error: () => {}
          });
        } else {
          this.syncComplianceForRun();
        }
      },
      error: () => {
        this.error = 'Could not load story data.';
        this.loading = false;
      }
    });
  }

  private findSceneContext(
    books: Book[],
    sceneId: string
  ): { book: Book; chapter: Chapter; scene: Scene } | null {
    for (const b of books) {
      for (const ch of b.chapters ?? []) {
        const sc = (ch.scenes ?? []).find((s) => s.id === sceneId);
        if (sc) {
          return { book: b, chapter: ch, scene: sc };
        }
      }
    }
    return null;
  }

  private syncFormFromScene(s: Scene): void {
    this.draftText = s.latestDraftText ?? '';
    this.pendingPostStateRaw = s.pendingPostStateJson ?? null;
    const endRaw = s.pendingPostStateJson ?? s.approvedStateTableJson ?? null;
    this.lastStateTableJson = endRaw ? formatJsonPretty(endRaw) : null;
  }

  /** Verdict fields for template (small list; safe to parse per change detection). */
  parsedVerdict(row: ComplianceEvaluation): ParsedComplianceVerdict {
    return parseComplianceVerdictJson(row.verdictJson ?? '{}', row.kind);
  }

  complianceKindLabel(kind: string): string {
    switch (kind) {
      case 'Quality':
        return 'Quality';
      case 'Transition':
        return 'Transition';
      case 'Compliance':
      default:
        return 'Compliance';
    }
  }

  private syncComplianceForRun(): void {
    const id = this.generationRunId;
    this.complianceSub?.unsubscribe();
    this.complianceSub = undefined;
    if (!id) {
      this.complianceEvaluations = [];
      this.draftReviewNotes = [];
      void this.disconnectComplianceHub();
      return;
    }
    this.complianceSub = this.odata.getComplianceEvaluationsForRun(id).subscribe({
      next: (rows) => {
        this.complianceEvaluations = rows;
      },
      error: () => {
        this.complianceEvaluations = [];
      }
    });
    this.connectComplianceHub(id);
  }

  private connectComplianceHub(runId: string): void {
    if (this.hubConnectedRunId === runId && this.hubConnection) {
      return;
    }
    if (this.hubConnectedRunId !== runId) {
      this.draftReviewNotes = [];
    }
    void this.hubConnection?.stop();
    this.hubConnection = null;
    this.hubConnectedRunId = runId;
    this.hubConnection = this.generation.connectToRun(runId, {
      onProgress: (eventName, payload) => {
        if (eventName !== 'DraftReviewNote') {
          return;
        }
        const step = (payload.step ?? '').trim();
        const detail = (payload.detail ?? '').trim();
        if (!detail) {
          return;
        }
        this.draftReviewNotes = [
          { step: step || 'Compliance / quality', detail },
          ...this.draftReviewNotes
        ];
      },
      onFinished: () => {}
    });
  }

  private async disconnectComplianceHub(): Promise<void> {
    await this.hubConnection?.stop();
    this.hubConnection = null;
    this.hubConnectedRunId = null;
  }

  backToSceneDesign(): void {
    void this.router.navigate(['/scenes']);
  }

  get draftTooLongForAnalysis(): boolean {
    return this.draftText.length > SceneDraftComponent.maxDraftCharsForAnalysis;
  }

  /** Banner while correct/finalize or draft recommendations run (not modal PATCH). */
  get draftLlmWorkingVisible(): boolean {
    return this.busy || this.recommendationsBusy;
  }

  get draftLlmWorkingLabel(): string {
    if (this.recommendationsBusy) return 'Analyzing draft…';
    return 'Model is working…';
  }

  get endStateSummary(): string {
    if (!this.lastStateTableJson?.trim()) return '';
    const t = this.lastStateTableJson.trim();
    return t.length <= 72 ? t : `${t.slice(0, 72)}…`;
  }

  clearDraftRecommendations(): void {
    this.draftRecommendations = [];
    this.recommendationsError = null;
    this.recommendationsBusy = false;
  }

  loadDraftRecommendations(): void {
    if (!this.sceneId || !this.draftText.trim()) {
      this.error = 'Need draft text to analyze.';
      return;
    }
    if (this.draftTooLongForAnalysis) {
      this.error = `Draft exceeds ${SceneDraftComponent.maxDraftCharsForAnalysis.toLocaleString()} characters (analysis limit).`;
      return;
    }
    this.recommendationsSub?.unsubscribe();
    this.recommendationsBusy = true;
    this.recommendationsError = null;
    this.error = null;
    this.recommendationsSub = this.sceneWorkflow.getDraftRecommendations(this.sceneId, this.draftText).subscribe({
      next: (res) => {
        this.draftRecommendations = res.items ?? [];
        this.recommendationsBusy = false;
      },
      error: (err: unknown) => {
        this.recommendationsBusy = false;
        const e = err as { error?: { message?: string } | string; message?: string };
        const body = e?.error;
        this.recommendationsError =
          typeof body === 'string'
            ? body
            : body && typeof body === 'object' && 'message' in body
              ? String((body as { message: string }).message)
              : e?.message ?? 'Request failed.';
      }
    });
  }

  cancelDraftRecommendations(): void {
    this.recommendationsSub?.unsubscribe();
    this.recommendationsBusy = false;
  }

  getSuggestionDiffPreview(item: DraftRecommendationItem): SuggestionDiffPreview | null {
    const n = getParagraphNeighborhood(this.draftText, item.paragraphStart, item.paragraphEnd);
    if (!n) return null;
    if (item.kind === 'replace') {
      return {
        mode: 'replace',
        beforeContext: n.beforeContext,
        problemSpan: n.problemSpan,
        afterContext: n.afterContext,
        replacementText: item.replacementText ?? ''
      };
    }
    if (item.kind === 'rewrite' && item.rewriteInstruction?.trim()) {
      return {
        mode: 'rewrite',
        beforeContext: n.beforeContext,
        problemSpan: n.problemSpan,
        afterContext: n.afterContext,
        instruction: item.rewriteInstruction.trim()
      };
    }
    return null;
  }

  selectRecommendedReplacementInDraft(item: DraftRecommendationItem): void {
    const range = getParagraphSelectionRangeInDraft(this.draftText, item.paragraphStart, item.paragraphEnd);
    if (!range) return;
    const ta = this.draftBody?.nativeElement;
    if (!ta) return;
    void Promise.resolve().then(() => {
      ta.focus();
      ta.setSelectionRange(range.start, range.end);
    });
  }

  applyRecommendationReplace(index: number): void {
    const item = this.draftRecommendations[index];
    if (!item || item.kind !== 'replace' || !item.replacementText?.trim()) return;
    this.draftText = applyParagraphReplace(
      this.draftText,
      item.paragraphStart,
      item.paragraphEnd,
      item.replacementText
    );
    this.dismissRecommendationAt(index);
    this.clearDraftSelection();
  }

  useRewriteInstruction(index: number): void {
    const item = this.draftRecommendations[index];
    if (!item || item.kind !== 'rewrite' || !item.rewriteInstruction?.trim()) return;
    this.correctInstruction = item.rewriteInstruction.trim();
    this.dismissRecommendationAt(index);
  }

  dismissRecommendationAt(index: number): void {
    this.draftRecommendations = this.draftRecommendations.filter((_, i) => i !== index);
  }

  finalizeDraft(): void {
    if (!this.sceneId || !this.generationRunId) return;
    this.reviewDraftSub?.unsubscribe();
    this.busy = true;
    this.error = null;
    this.reviewDraftSub = this.generation
      .finalizeGeneration(this.sceneId, {
        generationRunId: this.generationRunId,
        acceptedDraftText: this.draftText,
        approvedStateTableJson: null
      })
      .subscribe({
        next: (res) => {
          this.lastStateTableJson = res.stateTableJson ? formatJsonPretty(res.stateTableJson) : null;
          this.generationRunId = null;
          this.complianceEvaluations = [];
          this.draftReviewNotes = [];
          void this.disconnectComplianceHub();
          this.busy = false;
          this.clearDraftRecommendations();
          const nextId = res.nextSceneId ?? this.sceneId;
          void this.router.navigate(['/scenes'], { queryParams: { sceneId: nextId } });
        },
        error: () => {
          this.busy = false;
        }
      });
  }

  private clampDraftSelIdx(i: number): number {
    const len = this.draftText.length;
    return Math.min(Math.max(0, i), len);
  }

  draftSelectionHighlight(): boolean {
    if (this.draftReviewFocused) {
      return false;
    }
    const s = this.clampDraftSelIdx(this.savedDraftSelectionStart);
    const e = this.clampDraftSelIdx(this.savedDraftSelectionEnd);
    return s < e;
  }

  draftBeforeSelection(): string {
    return this.draftText.slice(0, this.clampDraftSelIdx(this.savedDraftSelectionStart));
  }

  draftSelectedSlice(): string {
    const s = this.clampDraftSelIdx(this.savedDraftSelectionStart);
    const e = this.clampDraftSelIdx(this.savedDraftSelectionEnd);
    return this.draftText.slice(s, Math.max(s, e));
  }

  draftAfterSelection(): string {
    return this.draftText.slice(this.clampDraftSelIdx(this.savedDraftSelectionEnd));
  }

  draftMirrorTransform(): string {
    return `translateY(-${this.draftScrollTop}px)`;
  }

  onDraftFocus(): void {
    this.draftReviewFocused = true;
  }

  onDraftScroll(): void {
    this.draftScrollTop = this.draftBody?.nativeElement?.scrollTop ?? 0;
  }

  onDraftBlur(): void {
    const ta = this.draftBody?.nativeElement;
    if (!ta) return;
    this.savedDraftSelectionStart = ta.selectionStart;
    this.savedDraftSelectionEnd = ta.selectionEnd;
    this.draftScrollTop = ta.scrollTop;
    this.draftReviewFocused = false;
  }

  cancelReviewDraftRequest(): void {
    this.reviewDraftSub?.unsubscribe();
    this.busy = false;
  }

  runCorrect(): void {
    if (!this.sceneId || !this.generationRunId) return;
    const ins = this.correctInstruction.trim();
    if (!ins) {
      this.error = 'Enter an instruction for the correction.';
      return;
    }
    this.draftTextSnapshotBeforeCorrect = this.draftText;
    this.pendingPostSnapshotBeforeCorrect = this.pendingPostStateRaw;
    this.lastStateTableJsonSnapshotBeforeCorrect = this.lastStateTableJson;
    this.reviewDraftSub?.unsubscribe();
    this.busy = true;
    this.error = null;
    const ta = this.draftBody?.nativeElement;
    let selectionStart: number | undefined;
    let selectionEnd: number | undefined;
    if (ta && document.activeElement === ta && ta.selectionStart !== ta.selectionEnd) {
      selectionStart = ta.selectionStart;
      selectionEnd = ta.selectionEnd;
    } else {
      const len = this.draftText.length;
      const s = Math.min(this.savedDraftSelectionStart, len);
      const e = Math.min(this.savedDraftSelectionEnd, len);
      if (s >= 0 && e <= len && s < e) {
        selectionStart = s;
        selectionEnd = e;
      }
    }
    const body: Parameters<GenerationService['correctDraft']>[1] = {
      generationRunId: this.generationRunId,
      instruction: ins,
      currentDraftText: this.draftText
    };
    if (selectionStart !== undefined && selectionEnd !== undefined) {
      body.selectionStart = selectionStart;
      body.selectionEnd = selectionEnd;
    }
    this.reviewDraftSub = this.generation.correctDraft(this.sceneId, body).subscribe({
      next: (res: CorrectDraftResponse) => {
        this.correctInstruction = '';
        this.draftText = res.correctedDraftText;
        this.correctModalProposedText = res.correctedDraftText;
        this.correctModalEditedText = res.correctedDraftText;
        this.pendingPostStateRaw = res.pendingPostStateJson ?? null;
        if (res.pendingPostStateJson) {
          this.lastStateTableJson = formatJsonPretty(res.pendingPostStateJson);
        } else {
          this.lastStateTableJson = null;
        }
        this.correctModalOpen = true;
        this.busy = false;
      },
      error: () => {
        this.busy = false;
      }
    });
  }

  acceptCorrectModal(): void {
    if (this.correctModalBusy) return;
    const edited = this.correctModalEditedText;
    const proposed = this.correctModalProposedText;
    if (!this.generationRunId) return;
    if (edited === proposed) {
      this.correctModalOpen = false;
      this.clearDraftSelection();
      return;
    }
    this.correctModalBusy = true;
    this.sceneWorkflow
      .patchScene(this.sceneId, {
        latestDraftText: edited,
        generationRunId: this.generationRunId,
        finalDraftText: edited
      })
      .subscribe({
        next: () => {
          this.draftText = edited;
          this.correctModalOpen = false;
          this.correctModalBusy = false;
          this.clearDraftSelection();
        },
        error: () => {
          this.correctModalBusy = false;
        }
      });
  }

  rejectCorrectModal(): void {
    if (this.correctModalBusy || !this.generationRunId) return;
    this.correctModalBusy = true;
    const body: Parameters<SceneWorkflowService['patchScene']>[1] = {
      latestDraftText: this.draftTextSnapshotBeforeCorrect,
      generationRunId: this.generationRunId,
      finalDraftText: this.draftTextSnapshotBeforeCorrect
    };
    if (this.pendingPostSnapshotBeforeCorrect === null) {
      body.clearPendingPostState = true;
    } else {
      body.pendingPostStateJson = this.pendingPostSnapshotBeforeCorrect;
    }
    this.sceneWorkflow.patchScene(this.sceneId, body).subscribe({
      next: () => {
        this.draftText = this.draftTextSnapshotBeforeCorrect;
        this.pendingPostStateRaw = this.pendingPostSnapshotBeforeCorrect;
        this.lastStateTableJson = this.lastStateTableJsonSnapshotBeforeCorrect;
        this.correctModalOpen = false;
        this.correctModalBusy = false;
        this.clearDraftSelection();
      },
      error: () => {
        this.correctModalBusy = false;
      }
    });
  }

  private clearDraftSelection(): void {
    this.savedDraftSelectionStart = 0;
    this.savedDraftSelectionEnd = 0;
    const ta = this.draftBody?.nativeElement;
    if (!ta) return;
    const len = this.draftText.length;
    void Promise.resolve().then(() => {
      ta.focus();
      ta.setSelectionRange(len, len);
    });
  }
}
