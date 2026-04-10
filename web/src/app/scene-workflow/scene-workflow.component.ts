import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HubConnection } from '@microsoft/signalr';
import { forkJoin, Subject, Subscription } from 'rxjs';
import { concatMap, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs/operators';
import { formatJsonPretty } from '../core/json-format';
import { Book, Chapter, Scene, WorldElement } from '../models/entities';
import { GenerationService, GenerationProgressPayload } from '../services/generation.service';
import { ODataService } from '../services/odata.service';
import { SceneWorkflowService } from '../services/scene-workflow.service';
import { WorldService } from '../services/world.service';
import { LlmWorkingIndicatorComponent } from '../shared/llm-working-indicator/llm-working-indicator.component';
import { UiIconComponent } from '../shared/ui-icon.component';
import { SCENE_WORKFLOW_FIELD_HELP } from './scene-workflow-field-help';

type SceneWorkflowPanelKey =
  | 'storyPosition'
  | 'manuscript'
  | 'synopsis'
  | 'voice'
  | 'beginningState'
  | 'worldElements'
  | 'generate';

export type GenerationLogKind = 'phase' | 'llm' | 'agent' | 'repair' | 'run' | 'other';

export interface GenerationLogEntry {
  id: string;
  at: Date;
  kind: GenerationLogKind;
  eventName: string;
  title: string;
  detail: string;
  elapsedMs?: number | null;
  stepDurationMs?: number | null;
  /** Server row id; load full text via OData when the user expands the log. */
  llmCallId?: string | null;
  llmDetailLoading?: boolean;
  llmDetailError?: string | null;
  llmPreview?: string | null;
  llmRequest?: string | null;
}

interface StoredStoryPosition {
  bookId: string;
  chapterId: string;
  sceneId: string;
}

@Component({
  selector: 'app-scene-workflow',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, UiIconComponent, LlmWorkingIndicatorComponent],
  templateUrl: './scene-workflow.component.html',
  styleUrl: './scene-workflow.component.scss'
})
export class SceneWorkflowComponent implements OnInit, OnDestroy {
  /** Native `title` tooltips for form controls. */
  readonly fieldHelp = SCENE_WORKFLOW_FIELD_HELP;

  /** Collapsible details sections — bound to [open] and synced on toggle. */
  panelOpen: Record<SceneWorkflowPanelKey, boolean> = {
    storyPosition: true,
    manuscript: true,
    synopsis: true,
    voice: true,
    beginningState: false,
    worldElements: true,
    generate: true
  };

  private readonly odata = inject(ODataService);
  private readonly generation = inject(GenerationService);
  private readonly world = inject(WorldService);
  private readonly sceneWorkflow = inject(SceneWorkflowService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  books: Book[] = [];
  selectedBookId = '';
  selectedChapterId = '';
  selectedSceneId = '';

  synopsis = '';
  instructions = '';
  expectedEnd = '';
  narrativePerspective = '';
  narrativeTense = '';
  beginningStateJson = '';
  chapterComplete = false;

  worldElementsPage: WorldElement[] = [];
  worldElementsTotalCount = 0;
  worldElementsSkip = 0;
  readonly worldElementsPageSize = 10;
  worldElementsSearchQuery = '';
  selectedWorldIds = new Set<string>();
  /** Modal: paged browse + server search (same OData as main panel). */
  modalWorldPage: WorldElement[] = [];
  modalWorldTotalCount = 0;
  modalWorldSkip = 0;
  readonly modalWorldPageSize = 10;
  modalWorldSearchQuery = '';
  worldBusy = false;

  suggestModalOpen = false;
  suggestBusy = false;
  modalWorkingIds = new Set<string>();

  generationLogEntries: GenerationLogEntry[] = [];
  generationLogModalOpen = false;
  error: string | null = null;
  busy = false;
  generationRunId: string | null = null;
  /** When set, a run is in AwaitingUserReview — link to the draft workspace. */
  pendingReviewRunId: string | null = null;

  // Unsubscribing cancels in-flight HttpClient requests.
  private generationStartSub: Subscription | undefined;
  private suggestSynopsisSub: Subscription | undefined;
  private llmCallDetailSub: Subscription | undefined;
  /** Entry currently loading LLM log detail (for teardown on unsubscribe). */
  private llmCallDetailEntry: GenerationLogEntry | null = null;

  /** Quality critic thresholds (0–100); persisted locally; sent with each generation run. */
  qualityAcceptMinScore = 75;
  qualityReviewOnlyMinScore = 55;

  /** Draft length band (words); persisted locally; sent as min/max overrides to the API. */
  draftTargetMinWords = 1500;
  draftTargetMaxWords = 2000;

  private hub: HubConnection | null = null;

  private static readonly storyPositionStorageKey = 'clf.sceneWorkflow.storyPosition';
  private static readonly qualityThresholdsStorageKey = 'clf.sceneWorkflow.qualityThresholds';

  private readonly destroy$ = new Subject<void>();
  private readonly worldElementsSearch$ = new Subject<string>();
  private readonly modalWorldSearch$ = new Subject<string>();

  ngOnInit(): void {
    this.worldElementsSearch$
      .pipe(debounceTime(400), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe(() => {
        this.worldElementsSkip = 0;
        this.loadWorldElementsPage();
      });
    this.modalWorldSearch$
      .pipe(debounceTime(400), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe(() => {
        this.modalWorldSkip = 0;
        this.loadModalWorldPage();
      });
    this.loadQualityThresholds();
    this.loadBooks();
  }

  ngOnDestroy(): void {
    this.generationStartSub?.unsubscribe();
    this.suggestSynopsisSub?.unsubscribe();
    this.llmCallDetailSub?.unsubscribe();
    if (this.llmCallDetailEntry) {
      this.llmCallDetailEntry.llmDetailLoading = false;
      this.llmCallDetailEntry = null;
    }
    this.destroy$.next();
    this.destroy$.complete();
    void this.hub?.stop();
  }

  onWorldElementsSearchInput(value: string): void {
    this.worldElementsSearch$.next(value);
  }

  onModalWorldSearchInput(value: string): void {
    this.modalWorldSearch$.next(value);
  }

  /** @param advanceToSceneId After reload, select this scene (e.g. new scene created on finalize). */
  loadBooks(advanceToSceneId?: string | null): void {
    this.error = null;
    this.odata.getBooksWithScenes().subscribe({
      next: (res) => {
        this.books = res.value ?? [];
        if (advanceToSceneId) {
          const pos = this.findStoryPositionForScene(advanceToSceneId);
          if (pos) {
            this.selectedBookId = pos.bookId;
            this.selectedChapterId = pos.chapterId;
            this.selectedSceneId = pos.sceneId;
          }
        }
        this.applyStoryPositionAfterBooksLoad();
        this.applyPendingQuerySceneId();
        this.syncFormFromScene();
        this.loadSceneWorldData();
        if (this.selectedSceneId) {
          this.loadWorkflowContext();
          this.refreshPendingReviewRun();
        } else {
          this.pendingReviewRunId = null;
        }
      },
      error: () => {}
    });
  }

  /** Deep link or return from draft finalize: `?sceneId=` selects the scene. */
  private applyPendingQuerySceneId(): void {
    const sid = this.route.snapshot.queryParamMap.get('sceneId');
    if (!sid) return;
    const pos = this.findStoryPositionForScene(sid);
    if (pos) {
      this.selectedBookId = pos.bookId;
      this.selectedChapterId = pos.chapterId;
      this.selectedSceneId = pos.sceneId;
      void this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
    }
  }

  private refreshPendingReviewRun(): void {
    if (!this.selectedSceneId) {
      this.pendingReviewRunId = null;
      return;
    }
    this.odata.getGenerationRunAwaitingReview(this.selectedSceneId).subscribe({
      next: (id) => {
        this.pendingReviewRunId = id;
      },
      error: () => {
        this.pendingReviewRunId = null;
      }
    });
  }

  private findStoryPositionForScene(sceneId: string): StoredStoryPosition | null {
    for (const b of this.books) {
      for (const ch of b.chapters ?? []) {
        if (ch.scenes?.some((s) => s.id === sceneId)) {
          return { bookId: b.id, chapterId: ch.id, sceneId };
        }
      }
    }
    return null;
  }

  /** Restore from localStorage on cold load; revalidate after reload; fall back to first book/chapter/scene. */
  private applyStoryPositionAfterBooksLoad(): void {
    if (this.books.length === 0) {
      this.selectedBookId = '';
      this.selectedChapterId = '';
      this.selectedSceneId = '';
      this.clearStoredStoryPosition();
      return;
    }

    if (this.selectedBookId) {
      const b = this.books.find((x) => x.id === this.selectedBookId);
      if (!b) {
        this.selectedBookId = '';
        this.selectedChapterId = '';
        this.selectedSceneId = '';
      } else {
        const chList = b.chapters ?? [];
        let ch = chList.find((c) => c.id === this.selectedChapterId);
        if (!ch) {
          ch = chList[0];
          this.selectedChapterId = ch?.id ?? '';
          this.selectedSceneId = ch?.scenes?.[0]?.id ?? '';
        } else {
          const scList = ch.scenes ?? [];
          if (!scList.some((s) => s.id === this.selectedSceneId)) {
            this.selectedSceneId = scList[0]?.id ?? '';
          }
        }
      }
    }

    if (!this.selectedBookId) {
      const saved = this.readStoredStoryPosition();
      if (saved && this.tryApplyStoryIds(saved.bookId, saved.chapterId, saved.sceneId)) {
        this.panelOpen.storyPosition = false;
      } else {
        this.pickDefaultStoryPosition();
      }
    }

    this.persistStoryPosition();
  }

  private pickDefaultStoryPosition(): void {
    const b = this.books[0];
    this.selectedBookId = b.id;
    const ch = b.chapters?.[0];
    this.selectedChapterId = ch?.id ?? '';
    const sc = ch?.scenes?.[0];
    this.selectedSceneId = sc?.id ?? '';
  }

  private tryApplyStoryIds(bookId: string, chapterId: string, sceneId: string): boolean {
    const b = this.books.find((x) => x.id === bookId);
    if (!b) return false;
    const chList = b.chapters ?? [];
    const ch = chList.find((c) => c.id === chapterId) ?? chList[0];
    if (!ch) return false;
    const scList = ch.scenes ?? [];
    const sc = scList.find((s) => s.id === sceneId) ?? scList[0];
    if (!sc) return false;
    this.selectedBookId = b.id;
    this.selectedChapterId = ch.id;
    this.selectedSceneId = sc.id;
    return true;
  }

  private readStoredStoryPosition(): StoredStoryPosition | null {
    try {
      const raw = localStorage.getItem(SceneWorkflowComponent.storyPositionStorageKey);
      if (!raw) return null;
      const o = JSON.parse(raw) as StoredStoryPosition;
      if (o && typeof o.bookId === 'string' && typeof o.chapterId === 'string' && typeof o.sceneId === 'string') {
        return o;
      }
    } catch {
      /* ignore */
    }
    return null;
  }

  private persistStoryPosition(): void {
    if (!this.selectedBookId) {
      localStorage.removeItem(SceneWorkflowComponent.storyPositionStorageKey);
      return;
    }
    const payload: StoredStoryPosition = {
      bookId: this.selectedBookId,
      chapterId: this.selectedChapterId,
      sceneId: this.selectedSceneId
    };
    localStorage.setItem(SceneWorkflowComponent.storyPositionStorageKey, JSON.stringify(payload));
  }

  private clearStoredStoryPosition(): void {
    localStorage.removeItem(SceneWorkflowComponent.storyPositionStorageKey);
  }

  chaptersForBook(): Chapter[] {
    const b = this.books.find((x) => x.id === this.selectedBookId);
    return b?.chapters ?? [];
  }

  scenesForChapter(): Scene[] {
    const ch = this.chaptersForBook().find((c) => c.id === this.selectedChapterId);
    const scenes = ch?.scenes ?? [];
    return [...scenes].sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
  }

  onBookChange(): void {
    const ch = this.chaptersForBook()[0];
    this.selectedChapterId = ch?.id ?? '';
    const sc = ch?.scenes?.[0];
    this.selectedSceneId = sc?.id ?? '';
    this.generationRunId = null;
    this.worldElementsSkip = 0;
    this.worldElementsSearchQuery = '';
    this.persistStoryPosition();
    this.syncFormFromScene();
    this.loadSceneWorldData();
    this.loadWorkflowContext();
    this.refreshPendingReviewRun();
  }

  onChapterChange(): void {
    const sc = this.scenesForChapter()[0];
    this.selectedSceneId = sc?.id ?? '';
    this.generationRunId = null;
    this.worldElementsSkip = 0;
    this.worldElementsSearchQuery = '';
    this.persistStoryPosition();
    this.syncFormFromScene();
    this.loadSceneWorldData();
    this.loadWorkflowContext();
    this.refreshPendingReviewRun();
  }

  onSceneChange(): void {
    this.generationRunId = null;
    this.worldElementsSkip = 0;
    this.persistStoryPosition();
    this.syncFormFromScene();
    this.loadSceneWorldData();
    this.loadWorkflowContext();
    this.refreshPendingReviewRun();
  }

  currentScene(): Scene | null {
    return this.scenesForChapter().find((s) => s.id === this.selectedSceneId) ?? null;
  }

  currentChapter(): Chapter | null {
    return this.chaptersForBook().find((c) => c.id === this.selectedChapterId) ?? null;
  }

  get currentBookTitle(): string {
    return this.books.find((x) => x.id === this.selectedBookId)?.title ?? '';
  }

  private truncate(s: string, max: number): string {
    const t = s.trim();
    if (t.length <= max) return t;
    return `${t.slice(0, max)}…`;
  }

  get storyPositionSummary(): string {
    const b = this.books.find((x) => x.id === this.selectedBookId);
    const ch = this.chaptersForBook().find((c) => c.id === this.selectedChapterId);
    const sc = this.currentScene();
    if (!b || !ch || !sc) return 'Choose book, chapter, and scene';
    return `${b.title} · ${ch.title} · ${sc.title}`;
  }

  get synopsisSectionSummary(): string {
    const t = this.synopsis.trim();
    return t ? this.truncate(t, 72) : 'No synopsis yet';
  }

  get voiceSummary(): string {
    const p = this.narrativePerspective.trim();
    const t = this.narrativeTense.trim();
    if (!p && !t) return 'Perspective and tense not set';
    if (p && t) return `${p} · ${t}`;
    return p || t;
  }

  get beginningStateSummary(): string {
    if (!this.beginningStateJson.trim()) return 'Empty — prior scene end-state when available';
    return this.truncate(this.beginningStateJson, 80);
  }

  get worldElementsSummary(): string {
    const linked = this.selectedWorldIds.size;
    const total = this.worldElementsTotalCount;
    const q = this.worldElementsSearchQuery.trim();
    if (q) {
      return `${linked} linked · ${total} match${total === 1 ? '' : 'es'} for “${this.truncate(q, 36)}”`;
    }
    return `${linked} linked · ${total} ${total === 1 ? 'entry' : 'entries'}`;
  }

  get generateSummary(): string {
    if (this.busy) return 'Generating…';
    const lo = this.draftTargetMinWords;
    const hi = this.draftTargetMaxWords;
    return `Ready · target ${lo}–${hi} words`;
  }

  /** Finalized prose for the selected scene (persists when you generate a new draft on this scene). */
  get sceneManuscript(): string {
    return this.currentScene()?.manuscriptText?.trim() ?? '';
  }

  get manuscriptSummary(): string {
    const t = this.sceneManuscript;
    return t ? this.truncate(t, 72) : '';
  }

  onPanelToggle(panel: SceneWorkflowPanelKey, ev: Event): void {
    const t = ev.target as HTMLDetailsElement | null;
    if (t?.tagName === 'DETAILS') {
      this.panelOpen[panel] = t.open;
    }
  }

  expandPanel(panel: SceneWorkflowPanelKey, ev: MouseEvent): void {
    ev.stopPropagation();
    ev.preventDefault();
    this.panelOpen[panel] = true;
  }

  collapsePanel(panel: SceneWorkflowPanelKey, ev: MouseEvent): void {
    ev.stopPropagation();
    ev.preventDefault();
    this.panelOpen[panel] = false;
  }

  private syncFormFromScene(): void {
    const s = this.currentScene();
    const ch = this.currentChapter();
    if (!s) {
      this.synopsis = '';
      this.instructions = '';
      this.expectedEnd = '';
      this.narrativePerspective = '';
      this.narrativeTense = '';
      this.beginningStateJson = '';
      this.chapterComplete = false;
      return;
    }
    this.synopsis = s.synopsis ?? '';
    this.instructions = s.instructions ?? '';
    this.expectedEnd = s.expectedEndStateNotes ?? '';
    this.narrativePerspective = s.narrativePerspective ?? '';
    this.narrativeTense = s.narrativeTense ?? '';
    this.beginningStateJson = formatJsonPretty(s.beginningStateJson ?? '');
    this.chapterComplete = ch?.isComplete ?? false;
    this.applyPanelDefaultsFromPersistedSceneFields(s);
  }

  /** Collapse Synopsis & Voice panels when those fields are already saved on the scene (not defaults from workflow). */
  private applyPanelDefaultsFromPersistedSceneFields(s: Scene): void {
    const synopsisPersisted = !!(
      s.synopsis?.trim() ||
      s.instructions?.trim() ||
      s.expectedEndStateNotes?.trim()
    );
    const voicePersisted = !!(s.narrativePerspective?.trim() || s.narrativeTense?.trim());
    this.panelOpen.synopsis = synopsisPersisted ? false : true;
    this.panelOpen.voice = voicePersisted ? false : true;
  }

  loadWorkflowContext(): void {
    if (!this.selectedSceneId) return;
    this.sceneWorkflow.getWorkflowContext(this.selectedSceneId).subscribe({
      next: (ctx) => {
        if (!this.narrativePerspective.trim() && ctx.defaultNarrativePerspective) {
          this.narrativePerspective = ctx.defaultNarrativePerspective;
        }
        if (!this.narrativeTense.trim() && ctx.defaultNarrativeTense) {
          this.narrativeTense = ctx.defaultNarrativeTense;
        }
        if (!this.beginningStateJson.trim() && ctx.previousSceneEndStateJson) {
          this.beginningStateJson = formatJsonPretty(ctx.previousSceneEndStateJson);
        }
      },
      error: () => {
        /* optional */
      }
    });
  }

  saveSceneFields(): void {
    if (!this.selectedSceneId) return;
    this.worldBusy = true;
    this.error = null;
    this.sceneWorkflow
      .patchScene(this.selectedSceneId, {
        synopsis: this.synopsis,
        instructions: this.instructions,
        expectedEndStateNotes: this.expectedEnd || null,
        narrativePerspective: this.narrativePerspective || null,
        narrativeTense: this.narrativeTense || null,
        beginningStateJson: this.beginningStateJson || null
      })
      .subscribe({
        next: () => {
          this.worldBusy = false;
          this.loadBooks();
        },
        error: () => {
          this.worldBusy = false;
        }
      });
  }

  toggleChapterComplete(): void {
    const ch = this.currentChapter();
    if (!ch) return;
    const next = !this.chapterComplete;
    this.sceneWorkflow.patchChapter(ch.id, { isComplete: next }).subscribe({
      next: () => {
        this.chapterComplete = next;
        this.loadBooks();
      },
      error: () => {}
    });
  }

  openSuggestModal(): void {
    if (!this.selectedBookId || !this.selectedSceneId) return;
    const syn = this.synopsis.trim();
    if (!syn) {
      this.error = 'Add a synopsis before suggesting world elements.';
      return;
    }
    this.error = null;
    this.suggestSynopsisSub?.unsubscribe();
    this.suggestBusy = true;
    this.modalWorkingIds = new Set(this.selectedWorldIds);
    this.modalWorldSearchQuery = '';
    this.suggestSynopsisSub = this.sceneWorkflow.suggestWorldElements(this.selectedBookId, syn).subscribe({
      next: (res) => {
        for (const id of res.elementIds ?? []) {
          this.modalWorkingIds.add(id);
        }
        this.suggestBusy = false;
        this.modalWorldSkip = 0;
        this.suggestModalOpen = true;
        this.loadModalWorldPage();
      },
      error: () => {
        this.suggestBusy = false;
      }
    });
  }

  cancelSuggestFromSynopsis(): void {
    this.suggestSynopsisSub?.unsubscribe();
  }

  closeSuggestModal(): void {
    this.suggestModalOpen = false;
  }

  applySuggestModal(): void {
    this.selectedWorldIds = new Set(this.modalWorkingIds);
    this.suggestModalOpen = false;
    this.saveSceneWorld();
  }

  toggleModalWorld(id: string, checked: boolean): void {
    if (checked) this.modalWorkingIds.add(id);
    else this.modalWorkingIds.delete(id);
    this.modalWorldPage = this.sortWorldElementsBySelectedFirst(
      this.modalWorldPage,
      this.modalWorkingIds
    );
  }

  worldElementsPrevPage(): void {
    this.worldElementsSkip = Math.max(0, this.worldElementsSkip - this.worldElementsPageSize);
    this.loadWorldElementsPage();
  }

  worldElementsNextPage(): void {
    if (!this.worldElementsHasNext()) return;
    this.worldElementsSkip += this.worldElementsPageSize;
    this.loadWorldElementsPage();
  }

  worldElementsHasNext(): boolean {
    return this.worldElementsSkip + this.worldElementsPage.length < this.worldElementsTotalCount;
  }

  worldElementsRangeLabel(): string {
    if (this.worldElementsTotalCount === 0) return '0 entries';
    const from = this.worldElementsSkip + 1;
    const to = this.worldElementsSkip + this.worldElementsPage.length;
    return `${from}–${to} of ${this.worldElementsTotalCount}`;
  }

  worldElementsFirstPage(): void {
    if (this.worldElementsSkip === 0) return;
    this.worldElementsSkip = 0;
    this.loadWorldElementsPage();
  }

  worldElementsLastPage(): void {
    const total = this.worldElementsTotalCount;
    if (total === 0) return;
    const ps = this.worldElementsPageSize;
    const lastSkip = Math.max(0, Math.floor((total - 1) / ps) * ps);
    if (this.worldElementsSkip === lastSkip) return;
    this.worldElementsSkip = lastSkip;
    this.loadWorldElementsPage();
  }

  worldElementsOnFirstPage(): boolean {
    return this.worldElementsSkip === 0;
  }

  worldElementsOnLastPage(): boolean {
    const total = this.worldElementsTotalCount;
    if (total === 0) return true;
    const ps = this.worldElementsPageSize;
    const lastSkip = Math.max(0, Math.floor((total - 1) / ps) * ps);
    return this.worldElementsSkip === lastSkip;
  }

  modalWorldPrevPage(): void {
    this.modalWorldSkip = Math.max(0, this.modalWorldSkip - this.modalWorldPageSize);
    this.loadModalWorldPage();
  }

  modalWorldNextPage(): void {
    if (!this.modalWorldHasNext()) return;
    this.modalWorldSkip += this.modalWorldPageSize;
    this.loadModalWorldPage();
  }

  modalWorldHasNext(): boolean {
    return this.modalWorldSkip + this.modalWorldPage.length < this.modalWorldTotalCount;
  }

  modalWorldRangeLabel(): string {
    if (this.modalWorldTotalCount === 0) return '0 entries';
    const from = this.modalWorldSkip + 1;
    const to = this.modalWorldSkip + this.modalWorldPage.length;
    return `${from}–${to} of ${this.modalWorldTotalCount}`;
  }

  modalWorldFirstPage(): void {
    if (this.modalWorldSkip === 0) return;
    this.modalWorldSkip = 0;
    this.loadModalWorldPage();
  }

  modalWorldLastPage(): void {
    const total = this.modalWorldTotalCount;
    if (total === 0) return;
    const ps = this.modalWorldPageSize;
    const lastSkip = Math.max(0, Math.floor((total - 1) / ps) * ps);
    if (this.modalWorldSkip === lastSkip) return;
    this.modalWorldSkip = lastSkip;
    this.loadModalWorldPage();
  }

  modalWorldOnFirstPage(): boolean {
    return this.modalWorldSkip === 0;
  }

  modalWorldOnLastPage(): boolean {
    const total = this.modalWorldTotalCount;
    if (total === 0) return true;
    const ps = this.modalWorldPageSize;
    const lastSkip = Math.max(0, Math.floor((total - 1) / ps) * ps);
    return this.modalWorldSkip === lastSkip;
  }

  /** Within the current page, list selected elements before the rest (stable by title, id). */
  private sortWorldElementsBySelectedFirst(
    elements: WorldElement[],
    selected: ReadonlySet<string>
  ): WorldElement[] {
    return [...elements].sort((a, b) => {
      const sa = selected.has(a.id) ? 0 : 1;
      const sb = selected.has(b.id) ? 0 : 1;
      if (sa !== sb) return sa - sb;
      const t = (a.title ?? '').localeCompare(b.title ?? '', undefined, { sensitivity: 'base' });
      if (t !== 0) return t;
      return a.id.localeCompare(b.id);
    });
  }

  private loadWorldElementsPage(): void {
    const bookId = this.selectedBookId;
    if (!bookId || !this.selectedSceneId) {
      this.worldElementsPage = [];
      this.worldElementsTotalCount = 0;
      return;
    }
    this.worldBusy = true;
    this.world
      .getWorldElementsPaged(bookId, {
        skip: this.worldElementsSkip,
        top: this.worldElementsPageSize,
        search: this.worldElementsSearchQuery.trim() || undefined
      })
      .subscribe({
        next: (res) => {
          this.worldElementsPage = this.sortWorldElementsBySelectedFirst(
            res.value ?? [],
            this.selectedWorldIds
          );
          this.worldElementsTotalCount = res.count ?? this.worldElementsPage.length;
          this.worldBusy = false;
        },
        error: () => {
          this.worldElementsPage = [];
          this.worldElementsTotalCount = 0;
          this.worldBusy = false;
        }
      });
  }

  private loadModalWorldPage(): void {
    const bookId = this.selectedBookId;
    if (!bookId || !this.suggestModalOpen) {
      this.modalWorldPage = [];
      this.modalWorldTotalCount = 0;
      return;
    }
    this.world
      .getWorldElementsPaged(bookId, {
        skip: this.modalWorldSkip,
        top: this.modalWorldPageSize,
        search: this.modalWorldSearchQuery.trim() || undefined
      })
      .subscribe({
        next: (res) => {
          this.modalWorldPage = this.sortWorldElementsBySelectedFirst(
            res.value ?? [],
            this.modalWorkingIds
          );
          this.modalWorldTotalCount = res.count ?? this.modalWorldPage.length;
        },
        error: () => {
          this.modalWorldPage = [];
          this.modalWorldTotalCount = 0;
        }
      });
  }

  loadSceneWorldData(): void {
    const bookId = this.selectedBookId;
    if (!bookId || !this.selectedSceneId) {
      this.worldElementsPage = [];
      this.worldElementsTotalCount = 0;
      this.selectedWorldIds = new Set();
      return;
    }
    this.worldBusy = true;
    forkJoin({
      page: this.world.getWorldElementsPaged(bookId, {
        skip: this.worldElementsSkip,
        top: this.worldElementsPageSize,
        search: this.worldElementsSearchQuery.trim() || undefined
      }),
      links: this.world.getSceneWorldElementIds(this.selectedSceneId)
    }).subscribe({
      next: ({ page, links }) => {
        const ids = links.value?.map((r) => r.worldElementId) ?? [];
        this.selectedWorldIds = new Set(ids);
        this.worldElementsPage = this.sortWorldElementsBySelectedFirst(
          page.value ?? [],
          this.selectedWorldIds
        );
        this.worldElementsTotalCount = page.count ?? this.worldElementsPage.length;
        this.worldBusy = false;
      },
      error: () => {
        this.worldElementsPage = [];
        this.worldElementsTotalCount = 0;
        this.selectedWorldIds = new Set();
        this.worldBusy = false;
      }
    });
  }

  isWorldSelected(id: string): boolean {
    return this.selectedWorldIds.has(id);
  }

  toggleWorld(id: string, ev: Event): void {
    const checked = (ev.target as HTMLInputElement).checked;
    if (checked) this.selectedWorldIds.add(id);
    else this.selectedWorldIds.delete(id);
    this.worldElementsPage = this.sortWorldElementsBySelectedFirst(
      this.worldElementsPage,
      this.selectedWorldIds
    );
  }

  saveSceneWorld(): void {
    if (!this.selectedSceneId) return;
    this.worldBusy = true;
    this.error = null;
    this.world.putSceneWorldElements(this.selectedSceneId, [...this.selectedWorldIds]).subscribe({
      next: () => {
        this.worldBusy = false;
        this.loadSceneWorldData();
      },
      error: () => {
        this.worldBusy = false;
      }
    });
  }

  formatDuration(ms: number | null | undefined): string {
    if (ms == null || ms < 0) return '—';
    if (ms < 1000) return `${Math.round(ms)} ms`;
    const s = ms / 1000;
    if (s < 60) return `${s.toFixed(1)} s`;
    const m = Math.floor(s / 60);
    const r = s % 60;
    return `${m}m ${r.toFixed(0)}s`;
  }

  /** SignalR / server event names (PascalCase) → spaced words for the log badge. */
  formatEventBadgeLabel(name: string): string {
    if (!name) return '';
    return name
      .replace(/_/g, ' ')
      .replace(/([a-z\d])([A-Z])/g, '$1 $2')
      .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
      .trim();
  }

  private nextLogId(): string {
    return globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`;
  }

  private pushProgressEvent(eventName: string, p: GenerationProgressPayload): void {
    const kind = this.mapEventToKind(eventName);
    const stepLabel = (p.step ?? '').replace(/_/g, ' ').trim() || eventName;
    const title =
      eventName === 'LlmRoundtrip'
        ? `LLM · ${stepLabel}`
        : eventName === 'RunStarted'
          ? 'Pipeline started'
          : eventName === 'AgentEditTurn'
            ? 'Agent edit'
            : eventName === 'RepairDraftApplied'
              ? 'Repair — draft updated'
              : eventName === 'DraftReviewNote'
                ? 'Compliance / quality note'
                : eventName === 'Local'
                ? 'Status'
                : stepLabel;
    const detail = (p.detail ?? '').trim();
    const entry: GenerationLogEntry = {
      id: this.nextLogId(),
      at: new Date(),
      kind,
      eventName,
      title,
      detail,
      elapsedMs: p.elapsedMs ?? null,
      stepDurationMs: p.stepDurationMs ?? null,
      llmCallId: p.llmCallId ?? null
    };
    this.generationLogEntries = [entry, ...this.generationLogEntries];
  }

  private mapEventToKind(eventName: string): GenerationLogKind {
    switch (eventName) {
      case 'LlmRoundtrip':
        return 'llm';
      case 'AgentEditTurn':
        return 'agent';
      case 'RepairAttempt':
      case 'RepairDraftApplied':
        return 'repair';
      case 'DraftReviewNote':
        return 'phase';
      case 'RunStarted':
      case 'RunFinished':
        return 'run';
      case 'StepStarted':
        return 'phase';
      case 'Local':
        return 'other';
      default:
        return 'other';
    }
  }

  openGenerationLogModal(): void {
    this.generationLogModalOpen = true;
  }

  closeGenerationLogModal(): void {
    this.generationLogModalOpen = false;
  }

  private loadQualityThresholds(): void {
    const stored = this.readGenerationPrefsFromStorage();
    if (stored) {
      this.qualityAcceptMinScore = stored.accept;
      this.qualityReviewOnlyMinScore = stored.review;
      this.draftTargetMinWords = stored.minWords;
      this.draftTargetMaxWords = stored.maxWords;
      return;
    }
    this.generation.getGenerationDefaults().subscribe({
      next: (s) => {
        this.qualityAcceptMinScore = s.qualityAcceptMinScore;
        this.qualityReviewOnlyMinScore = s.qualityReviewOnlyMinScore;
      },
      error: () => {
        /* keep constructor defaults */
      }
    });
  }

  private readGenerationPrefsFromStorage(): {
    accept: number;
    review: number;
    minWords: number;
    maxWords: number;
  } | null {
    try {
      const raw = globalThis.localStorage?.getItem(SceneWorkflowComponent.qualityThresholdsStorageKey);
      if (!raw) return null;
      const o = JSON.parse(raw) as {
        qualityAcceptMinScore?: unknown;
        qualityReviewOnlyMinScore?: unknown;
        draftTargetMinWords?: unknown;
        draftTargetMaxWords?: unknown;
      };
      if (typeof o.qualityAcceptMinScore !== 'number' || typeof o.qualityReviewOnlyMinScore !== 'number') {
        return null;
      }
      const minW =
        typeof o.draftTargetMinWords === 'number'
          ? SceneWorkflowComponent.clampDraftWordCount(o.draftTargetMinWords)
          : 1500;
      const maxW =
        typeof o.draftTargetMaxWords === 'number'
          ? SceneWorkflowComponent.clampDraftWordCount(o.draftTargetMaxWords)
          : 2000;
      const [a, b] = SceneWorkflowComponent.alignDraftWordRange(minW, maxW);
      return {
        accept: SceneWorkflowComponent.clampQualityScore(o.qualityAcceptMinScore),
        review: SceneWorkflowComponent.clampQualityScore(o.qualityReviewOnlyMinScore),
        minWords: a,
        maxWords: b
      };
    } catch {
      return null;
    }
  }

  private persistGenerationPrefs(): void {
    try {
      globalThis.localStorage?.setItem(
        SceneWorkflowComponent.qualityThresholdsStorageKey,
        JSON.stringify({
          qualityAcceptMinScore: this.qualityAcceptMinScore,
          qualityReviewOnlyMinScore: this.qualityReviewOnlyMinScore,
          draftTargetMinWords: this.draftTargetMinWords,
          draftTargetMaxWords: this.draftTargetMaxWords
        })
      );
    } catch {
      /* ignore quota */
    }
  }

  onQualityThresholdsChange(): void {
    this.qualityAcceptMinScore = SceneWorkflowComponent.clampQualityScore(this.qualityAcceptMinScore);
    this.qualityReviewOnlyMinScore = SceneWorkflowComponent.clampQualityScore(this.qualityReviewOnlyMinScore);
    this.persistGenerationPrefs();
  }

  onDraftWordRangeChange(): void {
    const [lo, hi] = SceneWorkflowComponent.alignDraftWordRange(
      SceneWorkflowComponent.clampDraftWordCount(this.draftTargetMinWords),
      SceneWorkflowComponent.clampDraftWordCount(this.draftTargetMaxWords)
    );
    this.draftTargetMinWords = lo;
    this.draftTargetMaxWords = hi;
    this.persistGenerationPrefs();
  }

  private static clampQualityScore(n: number): number {
    if (typeof n !== 'number' || Number.isNaN(n)) return 75;
    return Math.min(100, Math.max(0, n));
  }

  private static clampDraftWordCount(n: number): number {
    if (typeof n !== 'number' || Number.isNaN(n)) return 1500;
    return Math.min(20000, Math.max(100, Math.round(n)));
  }

  /** Ensures min ≤ max after clamping each bound. */
  private static alignDraftWordRange(minWords: number, maxWords: number): [number, number] {
    const a = SceneWorkflowComponent.clampDraftWordCount(minWords);
    const b = SceneWorkflowComponent.clampDraftWordCount(maxWords);
    return a <= b ? [a, b] : [b, a];
  }

  copyGenLogTextToClipboard(text: string | null | undefined): void {
    const t = text?.trim() ?? '';
    if (!t) return;
    this.error = null;
    if (!globalThis.navigator?.clipboard?.writeText) {
      this.error = 'Clipboard is not available in this browser context.';
      return;
    }
    void globalThis.navigator.clipboard.writeText(t).catch(() => {
      this.error = 'Could not copy to clipboard.';
    });
  }

  /** Fetches full request/response for a hub row that only carried `llmCallId`. */
  loadLlmCallDetail(entry: GenerationLogEntry): void {
    if (!entry.llmCallId || entry.llmDetailLoading) {
      return;
    }
    if (entry.llmRequest != null && entry.llmPreview != null) {
      return;
    }
    if (this.llmCallDetailEntry && this.llmCallDetailEntry !== entry) {
      this.llmCallDetailEntry.llmDetailLoading = false;
    }
    this.llmCallDetailSub?.unsubscribe();
    entry.llmDetailLoading = true;
    entry.llmDetailError = null;
    this.llmCallDetailEntry = entry;
    this.llmCallDetailSub = this.odata.getLlmCall(entry.llmCallId).subscribe({
      next: (row) => {
        entry.llmDetailLoading = false;
        this.llmCallDetailEntry = null;
        entry.llmRequest = row.requestJson;
        entry.llmPreview = row.responseText;
      },
      error: () => {
        entry.llmDetailLoading = false;
        this.llmCallDetailEntry = null;
        entry.llmDetailError = 'Could not load this LLM log from the server.';
      }
    });
  }

  startGeneration(): void {
    if (!this.selectedSceneId) {
      this.error = 'Select a scene first.';
      return;
    }
    this.busy = true;
    this.error = null;
    this.generationLogEntries = [];
    this.generationLogModalOpen = true;
    this.pushProgressEvent('Local', {
      runId: '',
      step: 'start',
      detail: `Saving scene world links, then starting generation (draft review mode, target ~${this.draftTargetMinWords}–${this.draftTargetMaxWords} words). Waiting for server run id…`,
      elapsedMs: 0,
      stepDurationMs: null,
      llmCallId: null
    });
    this.generationRunId = null;
    this.pendingReviewRunId = null;
    void this.hub?.stop();

    const sceneId = this.selectedSceneId;
    this.onQualityThresholdsChange();
    this.onDraftWordRangeChange();
    this.generationStartSub?.unsubscribe();
    this.generationStartSub = this.world
      .putSceneWorldElements(sceneId, [...this.selectedWorldIds])
      .pipe(
        concatMap(() =>
          this.generation.startGeneration(sceneId, {
            stopAfterDraft: true,
            minWordsOverride: this.draftTargetMinWords,
            maxWordsOverride: this.draftTargetMaxWords,
            qualityAcceptMinScore: this.qualityAcceptMinScore,
            qualityReviewOnlyMinScore: this.qualityReviewOnlyMinScore
          })
        )
      )
      .subscribe({
        next: (res) => {
          this.generationRunId = res.id;
          this.pushProgressEvent('Local', {
            runId: res.id,
            step: 'run id',
            detail: `Connected to generation run ${res.id}. Subscribing to live events…`,
            elapsedMs: null,
            stepDurationMs: null,
            llmCallId: null
          });
          this.hub = this.generation.connectToRun(res.id, {
            onProgress: (eventName, p) => this.pushProgressEvent(eventName, p),
            onFinished: (p) => this.onGenerationFinished(p)
          });
        },
        error: () => {
          this.busy = false;
          this.pushProgressEvent('Local', {
            runId: '',
            step: 'error',
            detail: 'Request failed — see error dialog for full details.',
            elapsedMs: null,
            stepDurationMs: null,
            llmCallId: null
          });
        }
      });
  }

  private onGenerationFinished(p: GenerationProgressPayload): void {
    this.pushProgressEvent('RunFinished', p);
    this.busy = false;
    void this.hub?.stop();
    this.hub = null;
    const step = p.step;
    if (step === 'AwaitingUserReview') {
      const sceneId = this.selectedSceneId;
      const runId = this.generationRunId;
      if (sceneId && runId) {
        void this.router.navigate(['/scenes', sceneId, 'draft'], { state: { generationRunId: runId } });
      }
      return;
    }
    this.loadBooks();
  }

  cancelDraftGeneration(): void {
    this.generationStartSub?.unsubscribe();
    this.generationStartSub = undefined;
    if (!this.selectedSceneId) {
      return;
    }
    this.error = null;
    if (!this.generationRunId) {
      this.busy = false;
      this.pushProgressEvent('Local', {
        runId: '',
        step: 'cancel',
        detail: 'Request cancelled.',
        elapsedMs: null,
        stepDurationMs: null,
        llmCallId: null
      });
      return;
    }
    this.generation.cancelGeneration(this.selectedSceneId, this.generationRunId).subscribe({
      next: () => {
        this.pushProgressEvent('Local', {
          runId: this.generationRunId ?? '',
          step: 'cancel',
          detail: 'Cancellation requested — stopping after the current step completes.',
          elapsedMs: null,
          stepDurationMs: null,
          llmCallId: null
        });
      },
      error: () => {}
    });
  }
}
