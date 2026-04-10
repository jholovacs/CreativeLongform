/** Matches API enum: 0 EarthMetric, 1 EarthUsCustomary, 2 Custom */
export type MeasurementPresetValue = 0 | 1 | 2;

export interface MeasurementCalendar {
  daysPerYear?: number;
  daysPerWeek?: number;
  monthNames?: string[];
  weekdayNames?: string[];
}

export interface MeasurementUnitRow {
  category: string;
  name: string;
  symbol?: string;
  definition: string;
  approximateSiNote?: string;
}

export interface MeasurementMoneyRow {
  name: string;
  definition: string;
  /** Nation, organization, or regime that issues or backs this currency. */
  authority?: string;
}

export interface MeasurementSystemPayload {
  schemaVersion: number;
  calendar?: MeasurementCalendar;
  units?: MeasurementUnitRow[];
  money?: MeasurementMoneyRow[];
  notes?: string;
}

export interface Book {
  id: string;
  title: string;
  storyToneAndStyle?: string;
  contentStyleNotes?: string | null;
  synopsis?: string | null;
  measurementPreset?: MeasurementPresetValue;
  measurementSystemJson?: string | null;
  createdAt?: string;
  chapters?: Chapter[];
}

export interface CreatedBookResponse {
  id: string;
  title: string;
}

export interface WorldLinkRow {
  id: string;
  fromWorldElementId: string;
  toWorldElementId: string;
  fromTitle: string;
  toTitle: string;
  relationLabel: string;
  relationDetail?: string | null;
}

export interface WorldLinksPage {
  totalCount: number;
  items: WorldLinkRow[];
}

export interface Chapter {
  id: string;
  bookId: string;
  order: number;
  title: string;
  isComplete?: boolean;
  scenes?: Scene[];
}

export interface Scene {
  id: string;
  chapterId: string;
  order: number;
  title: string;
  synopsis?: string;
  instructions: string;
  narrativePerspective?: string | null;
  narrativeTense?: string | null;
  beginningStateJson?: string | null;
  approvedStateTableJson?: string | null;
  /** Derived post-scene state after draft generation (before finalize). */
  pendingPostStateJson?: string | null;
  expectedEndStateNotes?: string | null;
  latestDraftText?: string | null;
  /** Accepted prose after finalize; not overwritten by a new generation run. */
  manuscriptText?: string | null;
}

/** API enum: 0 Scene, 1 WorldEvent */
export type TimelineEntryKindValue = 0 | 1;

export interface TimelineEntry {
  id: string;
  bookId: string;
  kind: TimelineEntryKindValue;
  sortKey: number;
  sceneId?: string | null;
  title: string;
  summary?: string | null;
  worldElementId?: string | null;
  scene?: Scene | null;
  worldElement?: Pick<WorldElement, 'id' | 'title' | 'kind'> | null;
  /** Exchange pair at this story-time beat. */
  currencyPairBase?: string | null;
  currencyPairQuote?: string | null;
  currencyPairAuthority?: string | null;
  currencyPairExchangeNote?: string | null;
}

export interface ODataCollection<T> {
  value: T[];
  /** Present when the request included `$count=true`. */
  '@odata.count'?: number;
}

export interface ODataPagedResult<T> {
  value: T[];
  count?: number;
}

export interface WorldElement {
  id: string;
  bookId: string;
  kind: string;
  title: string;
  slug?: string | null;
  summary: string;
  detail: string;
  status: string;
  provenance: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface SceneWorldElementRow {
  sceneId: string;
  worldElementId: string;
}

/** LLM-suggested link, resolved to element IDs (not created until user applies). */
export interface WorldBuildingSuggestedLink {
  fromWorldElementId: string;
  toWorldElementId: string;
  fromTitle: string;
  toTitle: string;
  relationLabel: string;
  /** Set when applying from the UI (optional). */
  relationDetail?: string | null;
}

export interface WorldBuildingApplyResult {
  createdElementIds: string[];
  /** @deprecated Links are no longer auto-created; use suggestedLinks. */
  createdLinkIds?: string[];
  suggestedLinks?: WorldBuildingSuggestedLink[];
}

export interface ApplySuggestedLinksResponse {
  createdCount: number;
}

/** LLM canon review for one world element: links + timeline attachments. */
export interface LinkCanonReviewProposal {
  id: string;
  kind: string;
  rationale: string;
  fromTitle?: string | null;
  toTitle?: string | null;
  relationLabel?: string | null;
  fromWorldElementId?: string | null;
  toWorldElementId?: string | null;
  linkId?: string | null;
  currentRelationLabel?: string | null;
  newRelationLabel?: string | null;
  timelineEntryId?: string | null;
  timelineEntryTitle?: string | null;
  currentWorldElementTitle?: string | null;
  proposedWorldElementTitle?: string | null;
  proposedWorldElementId?: string | null;
  clearWorldElementLink?: boolean;
}

export interface LinkCanonReviewResult {
  proposals: LinkCanonReviewProposal[];
}

export interface ApplyLinkCanonItem {
  kind: string;
  fromWorldElementId?: string | null;
  toWorldElementId?: string | null;
  relationLabel?: string | null;
  linkId?: string | null;
  newRelationLabel?: string | null;
  timelineEntryId?: string | null;
  worldElementId?: string | null;
  clearWorldElementId: boolean;
}

export interface LinkCanonApplyResult {
  linksAdded: number;
  linksRemoved: number;
  relationsUpdated: number;
  timelineEntriesUpdated: number;
}

/** OData `LlmCalls` row — persisted LLM request/response for a pipeline step. */
export interface LlmCall {
  id: string;
  generationRunId?: string | null;
  bookId?: string | null;
  step: string;
  model: string;
  requestJson: string;
  responseText: string;
  createdAt?: string;
}
