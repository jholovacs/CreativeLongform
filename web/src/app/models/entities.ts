/**
 * Client-side shapes for API and OData payloads used by the Angular app.
 * Align property names and enum numeric values with the .NET API so filters and JSON bodies stay compatible.
 */

/** API `MeasurementPreset` enum serialized as integer: 0 EarthMetric, 1 EarthUsCustomary, 2 Custom. */
export type MeasurementPresetValue = 0 | 1 | 2;

/** Optional fictional or alternate calendar embedded in {@link MeasurementSystemPayload}. */
export interface MeasurementCalendar {
  /** Days in one story year (world-building). */
  daysPerYear?: number;
  /** Days in one story week. */
  daysPerWeek?: number;
  /** Ordered month names for prompts and UI copy. */
  monthNames?: string[];
  /** Optional weekday labels. */
  weekdayNames?: string[];
}

/** One custom unit definition (length, mass, etc.) for LLM and author reference. */
export interface MeasurementUnitRow {
  /** Broad category, e.g. Length, Mass. */
  category: string;
  /** Canonical name of the unit in this world. */
  name: string;
  /** Short symbol if any (e.g. sp). */
  symbol?: string;
  /** Human-readable definition the model should respect. */
  definition: string;
  /** Optional note comparing to SI for intuition. */
  approximateSiNote?: string;
}

/** Fictional currency row for economic consistency in prompts. */
export interface MeasurementMoneyRow {
  /** Currency name. */
  name: string;
  /** What counts as one unit (e.g. silver coin). */
  definition: string;
  /** Nation, organization, or regime that issues or backs this currency. */
  authority?: string;
}

/**
 * Parsed `Book.measurementSystemJson` (custom preset). Serialized as JSON string on the server.
 * Feeds {@link MeasurementPromptFormatter} on the API and mirrors here for editors.
 */
export interface MeasurementSystemPayload {
  /** Schema version for forward-compatible parsing. */
  schemaVersion: number;
  /** Optional alternate calendar. */
  calendar?: MeasurementCalendar;
  /** Custom units list. */
  units?: MeasurementUnitRow[];
  /** Custom currencies list. */
  money?: MeasurementMoneyRow[];
  /** Freeform author notes to inject into prompts. */
  notes?: string;
}

/** OData `Books` entity as consumed by the SPA (subset of server fields). */
export interface Book {
  /** Primary key GUID string. */
  id: string;
  /** Display title. */
  title: string;
  /** High-level tone/style guidance for generation. */
  storyToneAndStyle?: string;
  /** Optional style constraints (voice, POV rules). */
  contentStyleNotes?: string | null;
  /** Book-level synopsis for navigation and LLM context. */
  synopsis?: string | null;
  /** See {@link MeasurementPresetValue}. */
  measurementPreset?: MeasurementPresetValue;
  /** JSON string for {@link MeasurementSystemPayload} when preset is Custom. */
  measurementSystemJson?: string | null;
  /** Creation timestamp (ISO), OData. */
  createdAt?: string;
  /** Assembled / edited full-book manuscript snapshot for workspace export or review. */
  manuscriptText?: string | null;
  /** Expanded chapters when `$expand` includes `Chapters`. */
  chapters?: Chapter[];
}

/** Response from book creation endpoint. */
export interface CreatedBookResponse {
  /** New book id. */
  id: string;
  /** Title as stored. */
  title: string;
}

/** One row from paginated world link browser API. */
export interface WorldLinkRow {
  /** Link id. */
  id: string;
  /** Source element id. */
  fromWorldElementId: string;
  /** Target element id. */
  toWorldElementId: string;
  /** Denormalized title for display. */
  fromTitle: string;
  /** Denormalized title for display. */
  toTitle: string;
  /** Short relation label (e.g. located_in). */
  relationLabel: string;
  /** Optional prose explaining the relation. */
  relationDetail?: string | null;
}

/** Paginated links result from REST API. */
export interface WorldLinksPage {
  /** Total rows for the filter (server-side count). */
  totalCount: number;
  /** Page of {@link WorldLinkRow}. */
  items: WorldLinkRow[];
}

/** OData `Chapters` entity. */
export interface Chapter {
  /** Primary key. */
  id: string;
  /** Owning book id. */
  bookId: string;
  /** Sort order within the book. */
  order: number;
  /** Chapter heading. */
  title: string;
  /** Author/marketing flag for completion. */
  isComplete?: boolean;
  /** Assembled / edited chapter manuscript snapshot. */
  manuscriptText?: string | null;
  /** Expanded scenes when `$expand` includes `Scenes`. */
  scenes?: Scene[];
}

/** OData `Scenes` entity — unit of generation and workflow. */
export interface Scene {
  /** Primary key. */
  id: string;
  /** Parent chapter id. */
  chapterId: string;
  /** Order within chapter. */
  order: number;
  /** Scene title. */
  title: string;
  /** Short beat summary for UI and prompts. */
  synopsis?: string;
  /** Author instructions fed to the generation pipeline. */
  instructions: string;
  /** Default POV for this scene (workflow). */
  narrativePerspective?: string | null;
  /** Default tense for this scene (workflow). */
  narrativeTense?: string | null;
  /** Serialized state at scene start (continuity). */
  beginningStateJson?: string | null;
  /** Author-approved state table after finalize. */
  approvedStateTableJson?: string | null;
  /** Latest derived state from draft generation before acceptance. */
  pendingPostStateJson?: string | null;
  /** Author notes on how the scene should end (continuity). */
  expectedEndStateNotes?: string | null;
  /** Current draft text from the latest generation run. */
  latestDraftText?: string | null;
  /** Accepted prose after finalize; not overwritten by a new run. */
  manuscriptText?: string | null;
}

/** API `TimelineEntryKind` enum: 0 Scene, 1 WorldEvent. */
export type TimelineEntryKindValue = 0 | 1;

/** OData `TimelineEntries` — ordering and annotations on the story timeline. */
export interface TimelineEntry {
  /** Primary key. */
  id: string;
  /** Owning book. */
  bookId: string;
  /** Discriminator: scene anchor vs world event. */
  kind: TimelineEntryKindValue;
  /** Sort key for stable ordering in UI and queries. */
  sortKey: number;
  /** When kind is Scene, linked scene id. */
  sceneId?: string | null;
  /** Display title on the timeline. */
  title: string;
  /** Optional description. */
  summary?: string | null;
  /** Optional linked world element for this beat. */
  worldElementId?: string | null;
  /** Expanded scene when requested. */
  scene?: Scene | null;
  /** Partial element for labels when expanded. */
  worldElement?: Pick<WorldElement, 'id' | 'title' | 'kind'> | null;
  /** FX/base currency code or name at this beat (story economy). */
  currencyPairBase?: string | null;
  /** Quote/secondary currency. */
  currencyPairQuote?: string | null;
  /** Who sets or backs the rate. */
  currencyPairAuthority?: string | null;
  /** Narrative note about the exchange context. */
  currencyPairExchangeNote?: string | null;
}

/** Standard OData collection wrapper. */
export interface ODataCollection<T> {
  /** Result rows. */
  value: T[];
  /** Present when the request included `$count=true`. */
  '@odata.count'?: number;
}

/** Simplified paged shape used by some client helpers. */
export interface ODataPagedResult<T> {
  /** Result rows. */
  value: T[];
  /** Optional total when provided by API layer. */
  count?: number;
}

/** OData `WorldElements` — lore, characters, places, etc. */
export interface WorldElement {
  /** Primary key. */
  id: string;
  /** Owning book. */
  bookId: string;
  /** Discriminator string matching server enum names. */
  kind: string;
  /** Short title. */
  title: string;
  /** URL/key slug if used. */
  slug?: string | null;
  /** Brief summary for lists and prompts. */
  summary: string;
  /** Longer canon text. */
  detail: string;
  /** Canon vs draft etc., server string. */
  status: string;
  /** How the element was created (manual, import, …). */
  provenance: string;
  createdAt?: string;
  updatedAt?: string;
}

/** Join row for scene ↔ world element association. */
export interface SceneWorldElementRow {
  sceneId: string;
  worldElementId: string;
}

/**
 * LLM-suggested link between two elements (preview only until user applies).
 */
export interface WorldBuildingSuggestedLink {
  fromWorldElementId: string;
  toWorldElementId: string;
  fromTitle: string;
  toTitle: string;
  relationLabel: string;
  /** Optional detail applied when user confirms in UI. */
  relationDetail?: string | null;
}

/** Result of applying world-building extraction/mutations. */
export interface WorldBuildingApplyResult {
  /** New element ids created in this apply. */
  createdElementIds: string[];
  /** @deprecated Links are no longer auto-created; use suggestedLinks. */
  createdLinkIds?: string[];
  /** Suggested links for the author to confirm. */
  suggestedLinks?: WorldBuildingSuggestedLink[];
}

/** Response after applying suggested links batch. */
export interface ApplySuggestedLinksResponse {
  /** Number of links created. */
  createdCount: number;
}

/** Single proposed change from link/canon review LLM pass. */
export interface LinkCanonReviewProposal {
  /** Stable id for UI selection. */
  id: string;
  /** Proposal type discriminator. */
  kind: string;
  /** Why the model suggests this change. */
  rationale: string;
  /** Source element title hint. */
  fromTitle?: string | null;
  /** Target element title hint. */
  toTitle?: string | null;
  /** Suggested relation label. */
  relationLabel?: string | null;
  /** Source element id when known. */
  fromWorldElementId?: string | null;
  /** Target element id when known. */
  toWorldElementId?: string | null;
  /** Existing link id if updating. */
  linkId?: string | null;
  /** Current label on an existing link. */
  currentRelationLabel?: string | null;
  /** Proposed new label. */
  newRelationLabel?: string | null;
  /** Timeline row to adjust. */
  timelineEntryId?: string | null;
  timelineEntryTitle?: string | null;
  /** Current attachment title on timeline. */
  currentWorldElementTitle?: string | null;
  /** Proposed attachment title. */
  proposedWorldElementTitle?: string | null;
  /** Proposed element id to attach. */
  proposedWorldElementId?: string | null;
  /** Whether to clear an element link on the timeline row. */
  clearWorldElementLink?: boolean;
}

/** LLM output wrapper for canon review. */
export interface LinkCanonReviewResult {
  proposals: LinkCanonReviewProposal[];
}

/** One user-approved mutation from canon review UI. */
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

/** Counts returned after applying canon mutations. */
export interface LinkCanonApplyResult {
  linksAdded: number;
  linksRemoved: number;
  relationsUpdated: number;
  timelineEntriesUpdated: number;
}

/** OData `LlmCalls` row — persisted LLM request/response for a pipeline step. */
export interface LlmCall {
  id: string;
  /** Owning generation run if any. */
  generationRunId?: string | null;
  /** Book scope for auditing. */
  bookId?: string | null;
  /** Pipeline step name. */
  step: string;
  /** Model id used. */
  model: string;
  /** Serialized request payload. */
  requestJson: string;
  /** Raw model output text. */
  responseText: string;
  /** Persist time. */
  createdAt?: string;
}
