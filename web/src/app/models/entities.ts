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
  measurementPreset?: MeasurementPresetValue;
  measurementSystemJson?: string | null;
  createdAt?: string;
  chapters?: Chapter[];
}

export interface Chapter {
  id: string;
  bookId: string;
  order: number;
  title: string;
  scenes?: Scene[];
}

export interface Scene {
  id: string;
  chapterId: string;
  order: number;
  title: string;
  instructions: string;
  expectedEndStateNotes?: string | null;
  latestDraftText?: string | null;
}

export interface ODataCollection<T> {
  value: T[];
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

export interface WorldBuildingApplyResult {
  createdElementIds: string[];
  createdLinkIds: string[];
}
