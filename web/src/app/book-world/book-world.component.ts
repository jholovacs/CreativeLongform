import { CommonModule } from '@angular/common';
import { Component, HostListener, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged, finalize, map, takeUntil } from 'rxjs';
import {
  ApplyLinkCanonItem,
  Book,
  LinkCanonReviewProposal,
  MeasurementCalendar,
  MeasurementPresetValue,
  MeasurementSystemPayload,
  MeasurementUnitRow,
  TimelineEntry,
  TimelineEntryKindValue,
  WorldBuildingSuggestedLink,
  WorldElement,
  WorldLinkRow
} from '../models/entities';
import { formatRelationLabel } from '../core/relation-label';
import { ODataService } from '../services/odata.service';
import { WorldService } from '../services/world.service';
import { UiIconComponent } from '../shared/ui-icon.component';
import { BOOK_WORLD_FIELD_HELP } from './book-world-field-help';
import { TimelineVizModalComponent } from './timeline-viz-modal.component';

type BookWorldPanelKey =
  | 'storyProfile'
  | 'timeline'
  | 'measurement'
  | 'llmWorld'
  | 'worldElements'
  | 'links';

/** One row in the link/timeline canon review modal (drafts for edit). */
interface LinkCanonUiRow {
  proposal: LinkCanonReviewProposal;
  accepted: boolean;
  editing: boolean;
  relationLabelDraft: string;
  newRelationLabelDraft: string;
  fromWorldElementIdDraft: string;
  toWorldElementIdDraft: string;
  /** Empty string = clear world-element link on timeline row. */
  proposedWorldElementIdDraft: string;
}

@Component({
  selector: 'app-book-world',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, TimelineVizModalComponent, UiIconComponent],
  templateUrl: './book-world.component.html',
  styleUrl: './book-world.component.scss'
})
export class BookWorldComponent implements OnInit, OnDestroy {
  /** Tooltips for editable fields (native `title`). */
  readonly fieldHelp = BOOK_WORLD_FIELD_HELP;

  private readonly route = inject(ActivatedRoute);
  private readonly odata = inject(ODataService);
  private readonly world = inject(WorldService);
  private readonly destroy$ = new Subject<void>();
  private readonly elementsSearch$ = new Subject<string>();
  private readonly timelineSearch$ = new Subject<string>();
  private readonly linksSearch$ = new Subject<string>();
  private readonly unitsSearch$ = new Subject<string>();
  private readonly moneySearch$ = new Subject<string>();

  readonly kinds = [
    'Geography',
    'Culture',
    'Lore',
    'Law',
    'SignificantEvent',
    'SocialSystem',
    'NovelSystem',
    'Character',
    'Other'
  ] as const;
  readonly statuses = ['Draft', 'Canon'] as const;

  bookId = '';
  book: Book | null = null;
  /** Full list for dropdowns (timeline + links). */
  pickerElements: WorldElement[] = [];
  /** Current OData page for the world elements table. */
  elementsPage: WorldElement[] = [];
  elementsTotalCount = 0;
  elementsSkip = 0;
  /** Fixed page size for world elements table. */
  readonly elementsPageSize = 10;
  elementsSearchQuery = '';

  timelineEntries: TimelineEntry[] = [];
  timelineTotalCount = 0;
  timelineSkip = 0;
  timelinePageSize = 20;
  timelineSearchQuery = '';

  linksPage: WorldLinkRow[] = [];
  linksTotalCount = 0;
  linksSkip = 0;
  readonly linksPageSize = 10;
  linksSearchQuery = '';
  /** Empty = all links; otherwise only links where this element is From or To. */
  linksWorldElementFilterId = '';
  /** API: relation | from | to | detail */
  linksSortKey: 'relation' | 'from' | 'to' | 'detail' = 'relation';
  linksSortDesc = false;

  tone = '';
  contentNotes = '';
  synopsis = '';
  extractText = '';
  generatePrompt = '';
  error: string | null = null;
  busy = false;

  editing: WorldElement | null = null;
  editKind = '';
  editTitle = '';
  editSlug = '';
  editSummary = '';
  editDetail = '';
  editStatus = 'Draft';

  newKind = 'Lore';
  newTitle = '';
  newSlug = '';
  newSummary = '';
  newDetail = '';
  newStatus = 'Draft';

  linkFromId = '';
  linkToId = '';
  linkLabel = '';
  linkRelationDetail = '';

  linkDetailModalOpen = false;
  linkDetailPatchId: string | null = null;
  linkDetailPatchLabel = '';
  linkDetailPatchText = '';

  newTimelineTitle = '';
  newTimelineSummary = '';
  newTimelineSortKey = '';
  newTimelineWorldElementId = '';
  newTimelineCurrencyBase = '';
  newTimelineCurrencyQuote = '';
  newTimelineCurrencyAuthority = '';
  newTimelineCurrencyExchangeNote = '';
  timelineSortDraft: Record<string, string> = {};
  /** Editable FX fields per timeline row (saved with Save pair). */
  timelineFxDraft: Record<string, { base: string; quote: string; authority: string; note: string }> = {};

  unitsSearchQuery = '';
  unitsSkip = 0;
  readonly unitsPageSize = 10;

  moneySearchQuery = '';
  moneySkip = 0;
  readonly moneyPageSize = 10;

  measurementPreset: MeasurementPresetValue = 0;
  measurementPayload: MeasurementSystemPayload = {
    schemaVersion: 1,
    units: [],
    money: []
  };
  monthNamesCsv = '';
  weekdayNamesCsv = '';
  calDaysPerYear: number | null = null;
  calDaysPerWeek: number | null = null;

  /** LLM-suggested links (modal); each row can be accepted or skipped. */
  linkSuggestionModalOpen = false;
  suggestedLinks: WorldBuildingSuggestedLink[] = [];
  suggestedLinksAccepted: boolean[] = [];
  /** Editable relation label per row (parallel to suggestedLinks). */
  suggestedLinkRelationDrafts: string[] = [];
  /** Editable from/to element ids (parallel to suggestedLinks). */
  suggestedLinkFromIdDrafts: string[] = [];
  suggestedLinkToIdDrafts: string[] = [];
  suggestedLinkDetailDrafts: string[] = [];
  suggestModalBusy = false;

  /** Shown while the suggest-links LLM call runs after save/create. */
  llmSuggestWorking = false;

  /** Link + timeline canon review (per world element row). */
  linkCanonModalOpen = false;
  linkCanonReviewBusy = false;
  linkCanonApplyBusy = false;
  linkCanonRows: LinkCanonUiRow[] = [];
  linkCanonFocusTitle = '';

  timelineVizOpen = false;
  timelineVizEntries: TimelineEntry[] = [];
  timelineVizLoading = false;

  /** Add world element dialog (modal). */
  addElementModalOpen = false;

  /** Glossary export: optional LLM alternate names, then download .md. */
  glossaryUseLlm = true;
  glossaryBusy = false;

  /** Collapsible <details> sections — bound to [open] and synced on toggle. */
  panelOpen: Record<BookWorldPanelKey, boolean> = {
    storyProfile: false,
    timeline: false,
    measurement: false,
    llmWorld: false,
    worldElements: false,
    links: false
  };

  private static readonly pendingLinksKey = (bookId: string) => `clf.pendingLinks.${bookId}`;

  ngOnInit(): void {
    this.route.paramMap
      .pipe(
        map((p) => p.get('bookId') ?? ''),
        distinctUntilChanged(),
        takeUntil(this.destroy$)
      )
      .subscribe((bookId) => {
        this.bookId = bookId;
        this.elementsSkip = 0;
        this.timelineSkip = 0;
        this.linksSkip = 0;
        this.linksWorldElementFilterId = '';
        this.linksSortKey = 'relation';
        this.linksSortDesc = false;
        this.unitsSkip = 0;
        this.moneySkip = 0;
        this.error = null;
        this.addElementModalOpen = false;
        this.editing = null;
        this.linkSuggestionModalOpen = false;
        this.suggestedLinks = [];
        this.suggestedLinksAccepted = [];
        this.suggestedLinkRelationDrafts = [];
        this.suggestedLinkFromIdDrafts = [];
        this.suggestedLinkToIdDrafts = [];
        this.suggestedLinkDetailDrafts = [];
        this.linkCanonModalOpen = false;
        this.linkCanonRows = [];
        this.linkCanonFocusTitle = '';
        const pending = sessionStorage.getItem(BookWorldComponent.pendingLinksKey(this.bookId));
        if (pending) {
          sessionStorage.removeItem(BookWorldComponent.pendingLinksKey(this.bookId));
          try {
            const parsed = JSON.parse(pending) as WorldBuildingSuggestedLink[];
            if (Array.isArray(parsed) && parsed.length > 0) {
              setTimeout(() => this.openSuggestedLinksModal(parsed), 0);
            }
          } catch {
            /* ignore */
          }
        }
        this.load();
      });
    this.elementsSearch$
      .pipe(debounceTime(400), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe(() => {
        this.elementsSkip = 0;
        this.loadElementsPage();
      });
    this.timelineSearch$
      .pipe(debounceTime(400), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe(() => {
        this.timelineSkip = 0;
        this.loadTimelinePage();
      });
    this.linksSearch$
      .pipe(debounceTime(400), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe(() => {
        this.linksSkip = 0;
        this.loadLinksPage();
      });
    this.unitsSearch$
      .pipe(debounceTime(400), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe(() => {
        this.unitsSkip = 0;
      });
    this.moneySearch$
      .pipe(debounceTime(400), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe(() => {
        this.moneySkip = 0;
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  @HostListener('document:keydown', ['$event'])
  onDocumentKeydown(ev: KeyboardEvent): void {
    if (ev.key !== 'Escape') return;
    if (this.addElementModalOpen) {
      ev.preventDefault();
      this.closeAddElementModal();
    } else if (this.editing) {
      ev.preventDefault();
      this.cancelEdit();
    }
  }

  openAddElementModal(): void {
    this.cancelEdit();
    this.newKind = 'Lore';
    this.newTitle = '';
    this.newSlug = '';
    this.newSummary = '';
    this.newDetail = '';
    this.newStatus = 'Draft';
    this.error = null;
    this.addElementModalOpen = true;
  }

  closeAddElementModal(): void {
    this.addElementModalOpen = false;
  }

  onPanelToggle(panel: BookWorldPanelKey, ev: Event): void {
    const t = ev.target as HTMLDetailsElement | null;
    if (t?.tagName === 'DETAILS') {
      this.panelOpen[panel] = t.open;
    }
  }

  expandPanel(panel: BookWorldPanelKey, ev: MouseEvent): void {
    ev.stopPropagation();
    ev.preventDefault();
    this.panelOpen[panel] = true;
  }

  collapsePanel(panel: BookWorldPanelKey, ev: MouseEvent): void {
    ev.stopPropagation();
    ev.preventDefault();
    this.panelOpen[panel] = false;
  }

  onElementsSearchInput(value: string): void {
    this.elementsSearch$.next(value);
  }

  onTimelineSearchInput(value: string): void {
    this.timelineSearch$.next(value);
  }

  onLinksSearchInput(value: string): void {
    this.linksSearch$.next(value);
  }

  onLinksFilterOrSortChange(): void {
    this.linksSkip = 0;
    this.loadLinksPage();
  }

  onUnitsSearchInput(value: string): void {
    this.unitsSearch$.next(value);
  }

  onMoneySearchInput(value: string): void {
    this.moneySearch$.next(value);
  }

  readonly formatRelationLabel = formatRelationLabel;

  openTimelineViz(): void {
    this.timelineVizLoading = true;
    this.error = null;
    this.world.getAllTimelineEntries(this.bookId).subscribe({
      next: (res) => {
        this.timelineVizEntries = res.value ?? [];
        this.timelineVizOpen = true;
        this.timelineVizLoading = false;
      },
      error: () => {
        this.timelineVizLoading = false;
      }
    });
  }

  closeTimelineViz(): void {
    this.timelineVizOpen = false;
    this.timelineVizEntries = [];
  }

  unitsFiltered(): { idx: number; u: MeasurementUnitRow }[] {
    const rows = this.measurementPayload.units ?? [];
    const q = this.unitsSearchQuery.trim().toLocaleLowerCase('en-US');
    const mapped = rows.map((u, idx) => ({ idx, u }));
    if (!q) return mapped;
    return mapped.filter(
      ({ u }) =>
        (u.category || '').toLocaleLowerCase('en-US').includes(q) ||
        (u.name || '').toLocaleLowerCase('en-US').includes(q) ||
        (u.symbol || '').toLocaleLowerCase('en-US').includes(q) ||
        (u.definition || '').toLocaleLowerCase('en-US').includes(q) ||
        (u.approximateSiNote || '').toLocaleLowerCase('en-US').includes(q)
    );
  }

  unitsPageView(): { idx: number; u: MeasurementUnitRow }[] {
    return this.unitsFiltered().slice(this.unitsSkip, this.unitsSkip + this.unitsPageSize);
  }

  unitsFilteredCount(): number {
    return this.unitsFiltered().length;
  }

  unitsPrevPage(): void {
    this.unitsSkip = Math.max(0, this.unitsSkip - this.unitsPageSize);
  }

  unitsNextPage(): void {
    if (!this.unitsHasNext()) return;
    this.unitsSkip += this.unitsPageSize;
  }

  unitsHasNext(): boolean {
    return this.unitsSkip + this.unitsPageSize < this.unitsFilteredCount();
  }

  unitsRangeLabel(): string {
    const n = this.unitsFilteredCount();
    if (n === 0) return '0 units';
    const from = this.unitsSkip + 1;
    const to = Math.min(this.unitsSkip + this.unitsPageSize, n);
    return `${from}–${to} of ${n}`;
  }

  removeUnitAtIndex(originalIndex: number): void {
    const u = [...(this.measurementPayload.units ?? [])];
    u.splice(originalIndex, 1);
    this.measurementPayload.units = u;
    if (this.unitsSkip >= this.unitsFilteredCount() && this.unitsSkip > 0) {
      this.unitsSkip = Math.max(0, this.unitsSkip - this.unitsPageSize);
    }
  }

  moneyFiltered(): { idx: number; m: { name: string; definition: string; authority?: string } }[] {
    const rows = this.measurementPayload.money ?? [];
    const q = this.moneySearchQuery.trim().toLocaleLowerCase('en-US');
    const mapped = rows.map((m, idx) => ({ idx, m }));
    if (!q) return mapped;
    return mapped.filter(
      ({ m }) =>
        (m.name || '').toLocaleLowerCase('en-US').includes(q) ||
        (m.definition || '').toLocaleLowerCase('en-US').includes(q) ||
        (m.authority || '').toLocaleLowerCase('en-US').includes(q)
    );
  }

  moneyPageView(): { idx: number; m: { name: string; definition: string; authority?: string } }[] {
    return this.moneyFiltered().slice(this.moneySkip, this.moneySkip + this.moneyPageSize);
  }

  moneyFilteredCount(): number {
    return this.moneyFiltered().length;
  }

  moneyPrevPage(): void {
    this.moneySkip = Math.max(0, this.moneySkip - this.moneyPageSize);
  }

  moneyNextPage(): void {
    if (!this.moneyHasNext()) return;
    this.moneySkip += this.moneyPageSize;
  }

  moneyHasNext(): boolean {
    return this.moneySkip + this.moneyPageSize < this.moneyFilteredCount();
  }

  moneyRangeLabel(): string {
    const n = this.moneyFilteredCount();
    if (n === 0) return '0 currencies';
    const from = this.moneySkip + 1;
    const to = Math.min(this.moneySkip + this.moneyPageSize, n);
    return `${from}–${to} of ${n}`;
  }

  removeMoneyAtIndex(originalIndex: number): void {
    const m = [...(this.measurementPayload.money ?? [])];
    m.splice(originalIndex, 1);
    this.measurementPayload.money = m;
    if (this.moneySkip >= this.moneyFilteredCount() && this.moneySkip > 0) {
      this.moneySkip = Math.max(0, this.moneySkip - this.moneyPageSize);
    }
  }

  onTimelinePageSizeChange(): void {
    this.timelineSkip = 0;
    this.loadTimelinePage();
  }

  elementsPrevPage(): void {
    this.elementsSkip = Math.max(0, this.elementsSkip - this.elementsPageSize);
    this.loadElementsPage();
  }

  elementsNextPage(): void {
    if (!this.elementsHasNext()) return;
    this.elementsSkip += this.elementsPageSize;
    this.loadElementsPage();
  }

  elementsHasNext(): boolean {
    return this.elementsSkip + this.elementsPage.length < this.elementsTotalCount;
  }

  elementsRangeLabel(): string {
    if (this.elementsTotalCount === 0) return '0 entries';
    const from = this.elementsSkip + 1;
    const to = this.elementsSkip + this.elementsPage.length;
    return `${from}–${to} of ${this.elementsTotalCount}`;
  }

  timelinePrevPage(): void {
    this.timelineSkip = Math.max(0, this.timelineSkip - this.timelinePageSize);
    this.loadTimelinePage();
  }

  timelineNextPage(): void {
    if (!this.timelineHasNext()) return;
    this.timelineSkip += this.timelinePageSize;
    this.loadTimelinePage();
  }

  timelineHasNext(): boolean {
    return this.timelineSkip + this.timelineEntries.length < this.timelineTotalCount;
  }

  timelineRangeLabel(): string {
    if (this.timelineTotalCount === 0) return '0 beats';
    const from = this.timelineSkip + 1;
    const to = this.timelineSkip + this.timelineEntries.length;
    return `${from}–${to} of ${this.timelineTotalCount}`;
  }

  linksPrevPage(): void {
    this.linksSkip = Math.max(0, this.linksSkip - this.linksPageSize);
    this.loadLinksPage();
  }

  linksNextPage(): void {
    if (!this.linksHasNext()) return;
    this.linksSkip += this.linksPageSize;
    this.loadLinksPage();
  }

  linksHasNext(): boolean {
    return this.linksSkip + this.linksPage.length < this.linksTotalCount;
  }

  linksRangeLabel(): string {
    if (this.linksTotalCount === 0) return '0 links';
    const from = this.linksSkip + 1;
    const to = this.linksSkip + this.linksPage.length;
    return `${from}–${to} of ${this.linksTotalCount}`;
  }

  elementsFirstPage(): void {
    if (this.elementsSkip === 0) return;
    this.elementsSkip = 0;
    this.loadElementsPage();
  }

  elementsLastPage(): void {
    const total = this.elementsTotalCount;
    if (total === 0) return;
    const ps = this.elementsPageSize;
    const lastSkip = Math.max(0, Math.floor((total - 1) / ps) * ps);
    if (this.elementsSkip === lastSkip) return;
    this.elementsSkip = lastSkip;
    this.loadElementsPage();
  }

  elementsOnFirstPage(): boolean {
    return this.elementsSkip === 0;
  }

  elementsOnLastPage(): boolean {
    const total = this.elementsTotalCount;
    if (total === 0) return true;
    const ps = this.elementsPageSize;
    const lastSkip = Math.max(0, Math.floor((total - 1) / ps) * ps);
    return this.elementsSkip === lastSkip;
  }

  timelineFirstPage(): void {
    if (this.timelineSkip === 0) return;
    this.timelineSkip = 0;
    this.loadTimelinePage();
  }

  timelineLastPage(): void {
    const total = this.timelineTotalCount;
    if (total === 0) return;
    const ps = this.timelinePageSize;
    const lastSkip = Math.max(0, Math.floor((total - 1) / ps) * ps);
    if (this.timelineSkip === lastSkip) return;
    this.timelineSkip = lastSkip;
    this.loadTimelinePage();
  }

  timelineOnFirstPage(): boolean {
    return this.timelineSkip === 0;
  }

  timelineOnLastPage(): boolean {
    const total = this.timelineTotalCount;
    if (total === 0) return true;
    const ps = this.timelinePageSize;
    const lastSkip = Math.max(0, Math.floor((total - 1) / ps) * ps);
    return this.timelineSkip === lastSkip;
  }

  linksFirstPage(): void {
    if (this.linksSkip === 0) return;
    this.linksSkip = 0;
    this.loadLinksPage();
  }

  linksLastPage(): void {
    const total = this.linksTotalCount;
    if (total === 0) return;
    const ps = this.linksPageSize;
    const lastSkip = Math.max(0, Math.floor((total - 1) / ps) * ps);
    if (this.linksSkip === lastSkip) return;
    this.linksSkip = lastSkip;
    this.loadLinksPage();
  }

  linksOnFirstPage(): boolean {
    return this.linksSkip === 0;
  }

  linksOnLastPage(): boolean {
    const total = this.linksTotalCount;
    if (total === 0) return true;
    const ps = this.linksPageSize;
    const lastSkip = Math.max(0, Math.floor((total - 1) / ps) * ps);
    return this.linksSkip === lastSkip;
  }

  unitsFirstPage(): void {
    if (this.unitsSkip === 0) return;
    this.unitsSkip = 0;
  }

  unitsLastPage(): void {
    const n = this.unitsFilteredCount();
    if (n === 0) return;
    const ps = this.unitsPageSize;
    const lastSkip = Math.max(0, Math.floor((n - 1) / ps) * ps);
    if (this.unitsSkip === lastSkip) return;
    this.unitsSkip = lastSkip;
  }

  unitsOnFirstPage(): boolean {
    return this.unitsSkip === 0;
  }

  unitsOnLastPage(): boolean {
    const n = this.unitsFilteredCount();
    if (n === 0) return true;
    const ps = this.unitsPageSize;
    const lastSkip = Math.max(0, Math.floor((n - 1) / ps) * ps);
    return this.unitsSkip === lastSkip;
  }

  moneyFirstPage(): void {
    if (this.moneySkip === 0) return;
    this.moneySkip = 0;
  }

  moneyLastPage(): void {
    const n = this.moneyFilteredCount();
    if (n === 0) return;
    const ps = this.moneyPageSize;
    const lastSkip = Math.max(0, Math.floor((n - 1) / ps) * ps);
    if (this.moneySkip === lastSkip) return;
    this.moneySkip = lastSkip;
  }

  moneyOnFirstPage(): boolean {
    return this.moneySkip === 0;
  }

  moneyOnLastPage(): boolean {
    const n = this.moneyFilteredCount();
    if (n === 0) return true;
    const ps = this.moneyPageSize;
    const lastSkip = Math.max(0, Math.floor((n - 1) / ps) * ps);
    return this.moneySkip === lastSkip;
  }

  private truncate(s: string, max: number): string {
    const t = s.trim();
    if (t.length <= max) return t;
    return `${t.slice(0, max)}…`;
  }

  get storyProfileSummary(): string {
    const t = this.tone?.trim();
    const s = this.synopsis?.trim();
    if (t) return this.truncate(t, 90);
    if (s) return this.truncate(s, 90);
    return 'Tone, Notes, Synopsis';
  }

  get timelineSummary(): string {
    const n = this.timelineTotalCount;
    return `${n} ${n === 1 ? 'beat' : 'beats'}`;
  }

  get measurementSummary(): string {
    const labels = ['Earth metric', 'Earth US customary', 'Custom'];
    const p = labels[this.measurementPreset] ?? 'Custom';
    const u = this.measurementPayload.units?.length ?? 0;
    const m = this.measurementPayload.money?.length ?? 0;
    const cal = this.calDaysPerYear != null || this.calDaysPerWeek != null;
    return `${p} · ${u} units · ${m} money${cal ? ' · calendar overrides' : ''}`;
  }

  get worldElementsSummary(): string {
    const n = this.elementsTotalCount;
    const q = this.elementsSearchQuery.trim();
    if (q) return `${n} matches for “${this.truncate(q, 40)}”`;
    return `${n} ${n === 1 ? 'entry' : 'entries'}`;
  }

  get linksSummary(): string {
    const n = this.linksTotalCount;
    const q = this.linksSearchQuery.trim();
    const wid = this.linksWorldElementFilterId.trim();
    const el = wid ? this.pickerElements.find((e) => e.id === wid) : null;
    const involving = el ? ` · ${el.title}` : '';
    if (q) return `${n} link matches for “${this.truncate(q, 40)}”${involving}`;
    if (el) return `${n} link${n === 1 ? '' : 's'} involving ${el.title}`;
    return `${n} ${n === 1 ? 'link' : 'links'}`;
  }

  get llmWorldSummary(): string {
    return 'Extract & Generate via Ollama';
  }

  get editEntrySummary(): string {
    const t = this.editTitle.trim();
    return t ? `Editing: ${this.truncate(t, 56)}` : 'Edit Entry';
  }

  timelineKindLabel(kind: TimelineEntryKindValue): string {
    return kind === 0 ? 'Scene' : 'World event';
  }

  timelineDisplayTitle(entry: TimelineEntry): string {
    if (entry.kind === 0 && entry.scene?.title) return entry.scene.title;
    return entry.title;
  }

  saveTimelineFx(entry: TimelineEntry): void {
    const d = this.timelineFxDraft[entry.id];
    if (!d) return;
    this.busy = true;
    this.error = null;
    const hasAny = !!(d.base.trim() || d.quote.trim() || d.authority.trim() || d.note.trim());
    const body = hasAny
      ? {
          currencyPairBase: d.base.trim() || null,
          currencyPairQuote: d.quote.trim() || null,
          currencyPairAuthority: d.authority.trim() || null,
          currencyPairExchangeNote: d.note.trim() || null
        }
      : { clearCurrencyPair: true };
    this.world.patchTimelineEntry(entry.id, body).subscribe({
        next: () => {
          this.busy = false;
          this.load();
        },
        error: () => {
          this.busy = false;
        }
      });
  }

  saveTimelineSort(entry: TimelineEntry): void {
    const raw = this.timelineSortDraft[entry.id]?.trim() ?? '';
    const n = Number(raw);
    if (raw === '' || Number.isNaN(n)) {
      this.timelineSortDraft[entry.id] = String(entry.sortKey);
      return;
    }
    this.busy = true;
    this.error = null;
    this.world.patchTimelineEntry(entry.id, { sortKey: n }).subscribe({
      next: () => {
        this.busy = false;
        this.load();
      },
      error: () => {
        this.busy = false;
      }
    });
  }

  deleteTimelineEntry(entry: TimelineEntry): void {
    if (entry.kind === 0) return;
    if (!confirm('Remove this world event from the timeline?')) return;
    this.busy = true;
    this.error = null;
    this.world.deleteTimelineEntry(entry.id).subscribe({
      next: () => {
        this.busy = false;
        this.load();
      },
      error: () => {
        this.busy = false;
      }
    });
  }

  createWorldTimelineEvent(): void {
    const title = this.newTimelineTitle.trim();
    if (!title) {
      this.error = 'Timeline event title is required.';
      return;
    }
    const body: {
      title: string;
      summary?: string | null;
      sortKey?: number;
      worldElementId?: string | null;
      currencyPairBase?: string | null;
      currencyPairQuote?: string | null;
      currencyPairAuthority?: string | null;
      currencyPairExchangeNote?: string | null;
    } = { title };
    const sum = this.newTimelineSummary.trim();
    if (sum) body.summary = sum;
    const skRaw = this.newTimelineSortKey.trim();
    if (skRaw) {
      const sk = Number(skRaw);
      if (!Number.isNaN(sk)) body.sortKey = sk;
    }
    if (this.newTimelineWorldElementId) body.worldElementId = this.newTimelineWorldElementId;
    const b = this.newTimelineCurrencyBase.trim();
    const q = this.newTimelineCurrencyQuote.trim();
    const a = this.newTimelineCurrencyAuthority.trim();
    const n = this.newTimelineCurrencyExchangeNote.trim();
    if (b) body.currencyPairBase = b;
    if (q) body.currencyPairQuote = q;
    if (a) body.currencyPairAuthority = a;
    if (n) body.currencyPairExchangeNote = n;
    this.busy = true;
    this.error = null;
    this.world.createWorldTimelineEvent(this.bookId, body).subscribe({
      next: () => {
        this.busy = false;
        this.newTimelineTitle = '';
        this.newTimelineSummary = '';
        this.newTimelineSortKey = '';
        this.newTimelineWorldElementId = '';
        this.newTimelineCurrencyBase = '';
        this.newTimelineCurrencyQuote = '';
        this.newTimelineCurrencyAuthority = '';
        this.newTimelineCurrencyExchangeNote = '';
        this.load();
      },
      error: () => {
        this.busy = false;
      }
    });
  }

  load(): void {
    this.error = null;
    this.odata.getBook(this.bookId).subscribe({
      next: (res) => {
        this.book = res.value?.[0] ?? null;
        this.tone = this.book?.storyToneAndStyle ?? '';
        this.contentNotes = this.book?.contentStyleNotes ?? '';
        this.synopsis = this.book?.synopsis ?? '';
        this.measurementPreset = (this.book?.measurementPreset ?? 0) as MeasurementPresetValue;
        this.measurementPayload = this.parseMeasurementJson(this.book?.measurementSystemJson);
        this.monthNamesCsv = (this.measurementPayload.calendar?.monthNames ?? []).join(', ');
        this.weekdayNamesCsv = (this.measurementPayload.calendar?.weekdayNames ?? []).join(', ');
        this.calDaysPerYear = this.measurementPayload.calendar?.daysPerYear ?? null;
        this.calDaysPerWeek = this.measurementPayload.calendar?.daysPerWeek ?? null;
      },
      error: () => {}
    });
    this.world.getWorldElements(this.bookId).subscribe({
      next: (res) => {
        this.pickerElements = res.value ?? [];
      },
      error: () => {
        this.pickerElements = [];
      }
    });
    this.loadElementsPage();
    this.loadTimelinePage();
    this.loadLinksPage();
  }

  private loadElementsPage(): void {
    this.world
      .getWorldElementsPaged(this.bookId, {
        skip: this.elementsSkip,
        top: this.elementsPageSize,
        search: this.elementsSearchQuery.trim() || undefined
      })
      .subscribe({
        next: (res) => {
          this.elementsPage = res.value ?? [];
          this.elementsTotalCount = res.count ?? this.elementsPage.length;
        },
        error: () => {
          this.elementsPage = [];
          this.elementsTotalCount = 0;
        }
      });
  }

  private loadTimelinePage(): void {
    this.world
      .getTimelineEntriesPaged(this.bookId, {
        skip: this.timelineSkip,
        top: this.timelinePageSize,
        search: this.timelineSearchQuery.trim() || undefined
      })
      .subscribe({
        next: (res) => {
          this.timelineEntries = res.value ?? [];
          this.timelineTotalCount = res.count ?? this.timelineEntries.length;
          this.timelineSortDraft = {};
          this.timelineFxDraft = {};
          for (const e of this.timelineEntries) {
            this.timelineSortDraft[e.id] = String(e.sortKey);
            this.timelineFxDraft[e.id] = {
              base: e.currencyPairBase ?? '',
              quote: e.currencyPairQuote ?? '',
              authority: e.currencyPairAuthority ?? '',
              note: e.currencyPairExchangeNote ?? ''
            };
          }
        },
        error: () => {
          this.timelineEntries = [];
          this.timelineTotalCount = 0;
          this.timelineSortDraft = {};
          this.timelineFxDraft = {};
        }
      });
  }

  private loadLinksPage(): void {
    this.world
      .getWorldLinksPaged(this.bookId, {
        skip: this.linksSkip,
        top: this.linksPageSize,
        search: this.linksSearchQuery.trim() || undefined,
        worldElementId: this.linksWorldElementFilterId.trim() || undefined,
        sortBy: this.linksSortKey,
        sortDesc: this.linksSortDesc
      })
      .subscribe({
        next: (res) => {
          this.linksPage = res.items ?? [];
          this.linksTotalCount = res.totalCount ?? this.linksPage.length;
        },
        error: () => {
          this.linksPage = [];
          this.linksTotalCount = 0;
        }
      });
  }

  private parseMeasurementJson(json: string | null | undefined): MeasurementSystemPayload {
    if (!json?.trim()) {
      return { schemaVersion: 1, units: [], money: [] };
    }
    try {
      const o = JSON.parse(json) as MeasurementSystemPayload;
      return {
        schemaVersion: o.schemaVersion ?? 1,
        calendar: o.calendar,
        units: o.units?.length ? [...o.units] : [],
        money: (o.money?.length ? [...o.money] : []).map((row) => ({
          ...row,
          name: row.name ?? '',
          definition: row.definition ?? '',
          authority: row.authority ?? ''
        })),
        notes: o.notes
      };
    } catch {
      return { schemaVersion: 1, units: [], money: [] };
    }
  }

  saveStoryProfile(): void {
    this.busy = true;
    this.error = null;
    this.world
      .patchStoryProfile(this.bookId, {
        storyToneAndStyle: this.tone,
        contentStyleNotes: this.contentNotes || null,
        synopsis: this.synopsis || null
      })
      .subscribe({
        next: () => {
          this.busy = false;
          this.load();
        },
        error: () => {
          this.busy = false;
        }
      });
  }

  saveMeasurement(): void {
    const cal: MeasurementCalendar = {};
    if (this.calDaysPerYear != null && !Number.isNaN(this.calDaysPerYear)) {
      cal.daysPerYear = this.calDaysPerYear;
    }
    if (this.calDaysPerWeek != null && !Number.isNaN(this.calDaysPerWeek)) {
      cal.daysPerWeek = this.calDaysPerWeek;
    }
    const months = this.splitCsv(this.monthNamesCsv);
    const weekdays = this.splitCsv(this.weekdayNamesCsv);
    if (months.length) cal.monthNames = months;
    if (weekdays.length) cal.weekdayNames = weekdays;
    if (Object.keys(cal).length === 0) {
      this.measurementPayload.calendar = undefined;
    } else {
      this.measurementPayload.calendar = cal;
    }

    const json = JSON.stringify(this.measurementPayload);
    this.busy = true;
    this.error = null;
    this.world
      .patchStoryProfile(this.bookId, {
        measurementPreset: this.measurementPreset,
        measurementSystemJson: json
      })
      .subscribe({
        next: () => {
          this.busy = false;
          this.load();
        },
        error: () => {
          this.busy = false;
        }
      });
  }

  private splitCsv(s: string): string[] {
    return s
      .split(',')
      .map((x) => x.trim())
      .filter(Boolean);
  }

  addUnitRow(): void {
    if (!this.measurementPayload.units) this.measurementPayload.units = [];
    this.measurementPayload.units.push({ category: '', name: '', definition: '' });
    const n = this.unitsFilteredCount();
    this.unitsSkip = Math.max(0, Math.floor((n - 1) / this.unitsPageSize) * this.unitsPageSize);
  }

  addMoneyRow(): void {
    if (!this.measurementPayload.money) this.measurementPayload.money = [];
    this.measurementPayload.money.push({ name: '', definition: '', authority: '' });
    const n = this.moneyFilteredCount();
    this.moneySkip = Math.max(0, Math.floor((n - 1) / this.moneyPageSize) * this.moneyPageSize);
  }

  runExtract(): void {
    this.busy = true;
    this.error = null;
    this.world.extractFromText(this.bookId, this.extractText).subscribe({
      next: (res) => {
        this.busy = false;
        this.extractText = '';
        if (res.suggestedLinks?.length) {
          this.openSuggestedLinksModal(res.suggestedLinks);
        }
        this.load();
      },
      error: () => {
        this.busy = false;
      }
    });
  }

  runGenerate(): void {
    this.busy = true;
    this.error = null;
    this.world.generateFromPrompt(this.bookId, this.generatePrompt).subscribe({
      next: (res) => {
        this.busy = false;
        this.generatePrompt = '';
        if (res.suggestedLinks?.length) {
          this.openSuggestedLinksModal(res.suggestedLinks);
        }
        this.load();
      },
      error: () => {
        this.busy = false;
      }
    });
  }

  openSuggestedLinksModal(links: WorldBuildingSuggestedLink[]): void {
    this.suggestedLinks = links;
    this.suggestedLinksAccepted = links.map(() => true);
    this.suggestedLinkRelationDrafts = links.map((l) => l.relationLabel ?? '');
    this.suggestedLinkFromIdDrafts = links.map((l) => l.fromWorldElementId);
    this.suggestedLinkToIdDrafts = links.map((l) => l.toWorldElementId);
    this.suggestedLinkDetailDrafts = links.map(() => '');
    this.linkSuggestionModalOpen = true;
  }

  closeSuggestedLinksModal(): void {
    this.linkSuggestionModalOpen = false;
    this.suggestedLinks = [];
    this.suggestedLinksAccepted = [];
    this.suggestedLinkRelationDrafts = [];
    this.suggestedLinkFromIdDrafts = [];
    this.suggestedLinkToIdDrafts = [];
    this.suggestedLinkDetailDrafts = [];
  }

  toggleSuggestedLink(index: number): void {
    const u = [...this.suggestedLinksAccepted];
    u[index] = !u[index];
    this.suggestedLinksAccepted = u;
  }

  acceptAllSuggestedLinks(): void {
    this.suggestedLinksAccepted = this.suggestedLinks.map(() => true);
  }

  rejectAllSuggestedLinks(): void {
    this.suggestedLinksAccepted = this.suggestedLinks.map(() => false);
  }

  applySuggestedLinksFromModal(): void {
    const toCreate: WorldBuildingSuggestedLink[] = [];
    for (let i = 0; i < this.suggestedLinks.length; i++) {
      if (!this.suggestedLinksAccepted[i]) continue;
      const link = this.suggestedLinks[i];
      const fromId = (this.suggestedLinkFromIdDrafts[i] ?? link.fromWorldElementId).trim();
      const toId = (this.suggestedLinkToIdDrafts[i] ?? link.toWorldElementId).trim();
      const relDraft = this.suggestedLinkRelationDrafts[i]?.trim();
      const rel = (relDraft || link.relationLabel || '').trim();
      const detailRaw = this.suggestedLinkDetailDrafts[i]?.trim() ?? '';
      if (!fromId || !toId || fromId === toId || !rel) {
        this.error =
          'Each selected link needs two different world elements and a relationship label.';
        return;
      }
      toCreate.push({
        ...link,
        fromWorldElementId: fromId,
        toWorldElementId: toId,
        relationLabel: rel,
        relationDetail: detailRaw || null
      });
    }
    if (toCreate.length === 0) {
      this.closeSuggestedLinksModal();
      return;
    }
    this.suggestModalBusy = true;
    this.error = null;
    this.world.applySuggestedLinks(this.bookId, toCreate).subscribe({
      next: () => {
        this.suggestModalBusy = false;
        this.closeSuggestedLinksModal();
        this.load();
      },
      error: () => {
        this.suggestModalBusy = false;
      }
    });
  }

  startLinkCanonReview(el: WorldElement): void {
    this.linkCanonFocusTitle = el.title;
    this.linkCanonReviewBusy = true;
    this.error = null;
    this.world
      .reviewLinkCanon(this.bookId, el.id)
      .pipe(finalize(() => (this.linkCanonReviewBusy = false)))
      .subscribe({
        next: (res) => {
          const list = res.proposals ?? [];
          this.linkCanonRows = list.map((p) => this.toLinkCanonUiRow(p));
          this.linkCanonModalOpen = true;
        },
        error: () => {}
      });
  }

  closeLinkCanonModal(): void {
    this.linkCanonModalOpen = false;
    this.linkCanonRows = [];
    this.linkCanonFocusTitle = '';
  }

  toggleLinkCanonAccepted(index: number): void {
    const r = this.linkCanonRows[index];
    if (r) r.accepted = !r.accepted;
  }

  setLinkCanonEditing(index: number, editing: boolean): void {
    const r = this.linkCanonRows[index];
    if (r) r.editing = editing;
  }

  acceptAllLinkCanon(): void {
    for (const row of this.linkCanonRows) row.accepted = true;
  }

  rejectAllLinkCanon(): void {
    for (const row of this.linkCanonRows) row.accepted = false;
  }

  linkCanonSummary(row: LinkCanonUiRow): string {
    const p = row.proposal;
    const k = p.kind;
    if (k === 'add_link') {
      const fromId = row.fromWorldElementIdDraft || p.fromWorldElementId;
      const toId = row.toWorldElementIdDraft || p.toWorldElementId;
      const fromEl = fromId ? this.pickerElements.find((e) => e.id === fromId) : undefined;
      const toEl = toId ? this.pickerElements.find((e) => e.id === toId) : undefined;
      const from = fromEl?.title ?? p.fromTitle ?? '?';
      const to = toEl?.title ?? p.toTitle ?? '?';
      const rel = row.relationLabelDraft.trim() || p.relationLabel || '?';
      return `Add link: ${from} —[${rel}]→ ${to}`;
    }
    if (k === 'remove_link') {
      const from = p.fromTitle ?? '?';
      const to = p.toTitle ?? '?';
      const rel = p.currentRelationLabel ?? '?';
      return `Remove link: ${from} —[${rel}]→ ${to}`;
    }
    if (k === 'change_relation') {
      const from = p.fromTitle ?? '?';
      const to = p.toTitle ?? '?';
      const cur = p.currentRelationLabel ?? '?';
      const neu = row.newRelationLabelDraft.trim() || p.newRelationLabel || '?';
      return `Change relation on ${from} → ${to}: “${cur}” → “${neu}”`;
    }
    if (k === 'set_timeline_link') {
      const title = p.timelineEntryTitle ?? '?';
      if (!row.proposedWorldElementIdDraft) {
        return `Timeline “${title}”: clear world-element link`;
      }
      const pick = this.pickerElements.find((e) => e.id === row.proposedWorldElementIdDraft);
      return `Timeline “${title}”: set linked entry to ${pick ? `${pick.kind}: ${pick.title}` : 'selected entry'}`;
    }
    return p.kind;
  }

  applyLinkCanonFromModal(): void {
    const items = this.buildLinkCanonApplyItems();
    if (items.length === 0) {
      this.closeLinkCanonModal();
      return;
    }
    this.linkCanonApplyBusy = true;
    this.error = null;
    this.world.applyLinkCanonReview(this.bookId, items).subscribe({
      next: () => {
        this.linkCanonApplyBusy = false;
        this.closeLinkCanonModal();
        this.load();
      },
      error: () => {
        this.linkCanonApplyBusy = false;
      }
    });
  }

  private toLinkCanonUiRow(p: LinkCanonReviewProposal): LinkCanonUiRow {
    const clear = p.clearWorldElementLink === true;
    return {
      proposal: p,
      accepted: true,
      editing: false,
      relationLabelDraft: p.relationLabel ?? '',
      newRelationLabelDraft: p.newRelationLabel ?? '',
      fromWorldElementIdDraft: p.fromWorldElementId ?? '',
      toWorldElementIdDraft: p.toWorldElementId ?? '',
      proposedWorldElementIdDraft: clear ? '' : (p.proposedWorldElementId ?? '')
    };
  }

  private buildLinkCanonApplyItems(): ApplyLinkCanonItem[] {
    const out: ApplyLinkCanonItem[] = [];
    for (const row of this.linkCanonRows) {
      if (!row.accepted) continue;
      const p = row.proposal;
      const k = p.kind;
      if (k === 'add_link') {
        const from = row.fromWorldElementIdDraft || p.fromWorldElementId;
        const to = row.toWorldElementIdDraft || p.toWorldElementId;
        const rel = (row.relationLabelDraft || p.relationLabel || '').trim();
        if (!from || !to || !rel) continue;
        out.push({
          kind: 'add_link',
          fromWorldElementId: from,
          toWorldElementId: to,
          relationLabel: rel,
          clearWorldElementId: false
        });
      } else if (k === 'remove_link') {
        const lid = p.linkId;
        if (!lid) continue;
        out.push({ kind: 'remove_link', linkId: lid, clearWorldElementId: false });
      } else if (k === 'change_relation') {
        const lid = p.linkId;
        const neu = (row.newRelationLabelDraft || p.newRelationLabel || '').trim();
        if (!lid || !neu) continue;
        out.push({ kind: 'change_relation', linkId: lid, newRelationLabel: neu, clearWorldElementId: false });
      } else if (k === 'set_timeline_link') {
        const te = p.timelineEntryId;
        if (!te) continue;
        const draft = row.proposedWorldElementIdDraft;
        if (!draft) {
          out.push({ kind: 'set_timeline_link', timelineEntryId: te, clearWorldElementId: true });
        } else {
          out.push({
            kind: 'set_timeline_link',
            timelineEntryId: te,
            worldElementId: draft,
            clearWorldElementId: false
          });
        }
      }
    }
    return out;
  }

  startEdit(el: WorldElement): void {
    this.addElementModalOpen = false;
    this.editing = el;
    this.editKind = el.kind;
    this.editTitle = el.title;
    this.editSlug = el.slug ?? '';
    this.editSummary = el.summary;
    this.editDetail = el.detail;
    this.editStatus = el.status;
  }

  cancelEdit(): void {
    this.editing = null;
  }

  saveEdit(): void {
    if (!this.editing) return;
    const elementId = this.editing.id;
    this.busy = true;
    this.error = null;
    this.world
      .patchWorldElement(elementId, {
        kind: this.editKind,
        title: this.editTitle,
        slug: this.editSlug || null,
        summary: this.editSummary,
        detail: this.editDetail,
        status: this.editStatus
      })
      .subscribe({
        next: () => {
          this.busy = false;
          this.editing = null;
          this.load();
          this.llmSuggestWorking = true;
          this.world
            .suggestLinksForElement(this.bookId, elementId)
            .pipe(finalize(() => (this.llmSuggestWorking = false)))
            .subscribe({
              next: (suggestions) => {
                if (suggestions.length) {
                  this.openSuggestedLinksModal(suggestions);
                }
              }
            });
        },
        error: () => {
          this.busy = false;
        }
      });
  }

  deleteElement(el: WorldElement): void {
    if (!confirm(`Delete “${el.title}”? Links involving this entry will be removed.`)) return;
    this.busy = true;
    this.error = null;
    this.world.deleteWorldElement(el.id).subscribe({
      next: () => {
        this.busy = false;
        if (this.editing?.id === el.id) this.editing = null;
        this.load();
      },
      error: () => {
        this.busy = false;
      }
    });
  }

  createElement(): void {
    if (!this.newTitle.trim()) {
      this.error = 'New entry needs a title.';
      return;
    }
    this.busy = true;
    this.error = null;
    this.world
      .createWorldElement(this.bookId, {
        kind: this.newKind,
        title: this.newTitle.trim(),
        slug: this.newSlug.trim() || undefined,
        summary: this.newSummary,
        detail: this.newDetail,
        status: this.newStatus
      })
      .subscribe({
        next: (created) => {
          this.busy = false;
          this.newTitle = '';
          this.newSlug = '';
          this.newSummary = '';
          this.newDetail = '';
          this.newKind = 'Lore';
          this.newStatus = 'Draft';
          this.addElementModalOpen = false;
          this.load();
          this.llmSuggestWorking = true;
          this.world
            .suggestLinksForElement(this.bookId, created.id)
            .pipe(finalize(() => (this.llmSuggestWorking = false)))
            .subscribe({
              next: (suggestions) => {
                if (suggestions.length) {
                  this.openSuggestedLinksModal(suggestions);
                }
              }
            });
        },
        error: () => {
          this.busy = false;
        }
      });
  }

  addLink(): void {
    if (!this.linkFromId || !this.linkToId || !this.linkLabel.trim()) {
      this.error = 'Pick two entries and a relation label.';
      return;
    }
    if (this.linkFromId === this.linkToId) {
      this.error = 'Link must connect two different entries.';
      return;
    }
    this.busy = true;
    this.error = null;
    const detail = this.linkRelationDetail.trim();
    this.world
      .createWorldLink(this.bookId, {
        fromWorldElementId: this.linkFromId,
        toWorldElementId: this.linkToId,
        relationLabel: this.linkLabel.trim(),
        relationDetail: detail ? detail : null
      })
      .subscribe({
        next: () => {
          this.busy = false;
          this.linkLabel = '';
          this.linkRelationDetail = '';
          this.load();
        },
        error: () => {
          this.busy = false;
        }
      });
  }

  linkDetailCell(d: string | null | undefined): string {
    const t = d?.trim() ?? '';
    if (!t) return '—';
    return this.truncate(t, 72);
  }

  openLinkDetailModal(row: WorldLinkRow): void {
    this.linkDetailPatchId = row.id;
    this.linkDetailPatchLabel = row.relationLabel?.trim() ?? '';
    this.linkDetailPatchText = row.relationDetail?.trim() ?? '';
    this.linkDetailModalOpen = true;
    this.error = null;
  }

  closeLinkDetailModal(): void {
    this.linkDetailModalOpen = false;
    this.linkDetailPatchId = null;
    this.linkDetailPatchLabel = '';
    this.linkDetailPatchText = '';
  }

  saveLinkDetail(): void {
    if (!this.linkDetailPatchId) return;
    const label = this.linkDetailPatchLabel.trim();
    if (!label) {
      this.error = 'Relation label is required.';
      return;
    }
    if (label.length > 128) {
      this.error = 'Relation label must be at most 128 characters.';
      return;
    }
    const text = this.linkDetailPatchText.trim();
    const detailPayload = text.length === 0 ? null : text;
    if (detailPayload !== null && detailPayload.length > 4000) {
      this.error = 'Relationship detail must be at most 4000 characters.';
      return;
    }
    this.busy = true;
    this.error = null;
    this.world.patchWorldLink(this.linkDetailPatchId, {
      relationLabel: label,
      relationDetail: detailPayload
    }).subscribe({
      next: () => {
        this.busy = false;
        this.closeLinkDetailModal();
        this.loadLinksPage();
      },
      error: () => {
        this.busy = false;
      }
    });
  }

  downloadGlossary(): void {
    this.glossaryBusy = true;
    this.error = null;
    this.world.getGlossaryMarkdown(this.bookId, this.glossaryUseLlm).subscribe({
      next: (md) => {
        this.glossaryBusy = false;
        const base = this.book?.title?.trim()
          ? BookWorldComponent.sanitizeFileName(this.book.title)
          : 'glossary';
        const blob = new Blob([md], { type: 'text/markdown;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${base}-glossary.md`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: () => {
        this.glossaryBusy = false;
      }
    });
  }

  private static sanitizeFileName(s: string): string {
    const t = s.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '_').trim().slice(0, 120);
    return t || 'glossary';
  }

  removeLink(row: WorldLinkRow): void {
    if (!confirm('Remove this link?')) return;
    this.busy = true;
    this.error = null;
    this.world.deleteWorldLink(row.id).subscribe({
      next: () => {
        this.busy = false;
        this.load();
      },
      error: () => {
        this.busy = false;
      }
    });
  }
}
