import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { apiBaseUrl } from '../core/api-config';
import { formatBytes } from '../core/format-bytes';
import {
  OllamaModelAssignmentsPatch,
  OllamaModelChangeLogDto,
  OllamaModelsService,
  OllamaPreferencesResponse,
  OllamaPullStreamLine
} from '../services/ollama-models.service';
import { LlmWorkingIndicatorComponent } from '../shared/llm-working-indicator/llm-working-indicator.component';
import { UiIconComponent } from '../shared/ui-icon.component';
import { OLLAMA_MODELS_FIELD_HELP } from './ollama-models-field-help';

type LogSortKey = 'when' | 'role' | 'previous' | 'new' | 'source';

type OllamaCollapsiblePanelKey = 'pull' | 'import' | 'log';

@Component({
  selector: 'app-ollama-models',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, UiIconComponent, LlmWorkingIndicatorComponent],
  templateUrl: './ollama-models.component.html',
  styleUrl: './ollama-models.component.scss'
})
export class OllamaModelsComponent implements OnInit {
  private readonly ollama = inject(OllamaModelsService);

  /** Shown when explaining where the UI expects the API (dev: `environment.apiBaseUrl`). */
  readonly apiTargetDisplay = apiBaseUrl || 'same origin as this page (e.g. Docker on port 8080)';

  readonly help = OLLAMA_MODELS_FIELD_HELP;

  /** Display disk / VRAM sizes from the API. */
  readonly formatBytes = formatBytes;

  /** Rows per page in the change log (fixed max). */
  readonly logPageSize = 10;

  loading = true;
  /** Local validation only (HTTP failures use the global error modal). */
  inlineError: string | null = null;
  /** True when the initial GET /preferences failed (show retry + help). */
  loadFailed = false;
  busy = false;
  prefs: OllamaPreferencesResponse | null = null;

  writerInput = '';
  criticInput = '';
  agentInput = '';
  worldBuildingInput = '';
  preStateInput = '';
  postStateInput = '';

  pullModel = '';
  /** Live status while a streamed library pull is in progress (NDJSON lines from Ollama). */
  pullProgress: { statusLine: string; percent: number | null } | null = null;
  importUrl = '';
  importName = '';

  /** Collapsible <details> sections — bound to [open] and synced on toggle (all closed by default). */
  panelOpen: Record<OllamaCollapsiblePanelKey, boolean> = {
    pull: false,
    import: false,
    log: false
  };

  /** Raw rows from API (up to server max). */
  private readonly logRowsAll = signal<OllamaModelChangeLogDto[]>([]);

  /** Search across time, role labels, model names, and source. */
  readonly logSearch = signal('');

  /** `null` = any role. */
  readonly logRoleFilter = signal<number | null>(null);

  /** Empty string = any source. */
  readonly logSourceFilter = signal('');

  readonly logSortKey = signal<LogSortKey>('when');
  readonly logSortDir = signal<'asc' | 'desc'>('desc');

  /** 1-based page index. */
  readonly logPage = signal(1);

  readonly logFiltered = computed(() => {
    const rows = this.logRowsAll();
    const q = this.logSearch().trim().toLowerCase();
    const role = this.logRoleFilter();
    const src = this.logSourceFilter().trim().toLowerCase();
    return rows.filter((r) => {
      if (role !== null && r.role !== role) {
        return false;
      }
      if (src && !r.source.toLowerCase().includes(src)) {
        return false;
      }
      if (!q) {
        return true;
      }
      const hay = [
        r.occurredAt,
        this.roleLabel(r.role),
        r.previousModel ?? '',
        r.newModel,
        r.source
      ]
        .join(' ')
        .toLowerCase();
      return hay.includes(q);
    });
  });

  readonly logSorted = computed(() => {
    const rows = [...this.logFiltered()];
    const key = this.logSortKey();
    const dir = this.logSortDir();
    const mult = dir === 'asc' ? 1 : -1;
    rows.sort((a, b) => {
      let cmp = 0;
      switch (key) {
        case 'when':
          cmp = new Date(a.occurredAt).getTime() - new Date(b.occurredAt).getTime();
          break;
        case 'role':
          cmp = a.role - b.role;
          break;
        case 'previous':
          cmp = (a.previousModel ?? '').localeCompare(b.previousModel ?? '', undefined, { sensitivity: 'base' });
          break;
        case 'new':
          cmp = a.newModel.localeCompare(b.newModel, undefined, { sensitivity: 'base' });
          break;
        case 'source':
          cmp = a.source.localeCompare(b.source, undefined, { sensitivity: 'base' });
          break;
      }
      return cmp * mult;
    });
    return rows;
  });

  readonly logTotalPages = computed(() => Math.max(1, Math.ceil(this.logSorted().length / this.logPageSize)));

  readonly logPaged = computed(() => {
    const sorted = this.logSorted();
    const totalPages = this.logTotalPages();
    const page = Math.min(Math.max(1, this.logPage()), totalPages);
    const start = (page - 1) * this.logPageSize;
    return sorted.slice(start, start + this.logPageSize);
  });

  readonly logRange = computed(() => {
    const total = this.logSorted().length;
    const totalPages = this.logTotalPages();
    const page = Math.min(Math.max(1, this.logPage()), totalPages);
    if (total === 0) {
      return { start: 0, end: 0, total: 0, page, totalPages };
    }
    const start = (page - 1) * this.logPageSize + 1;
    const end = Math.min(page * this.logPageSize, total);
    return { start, end, total, page, totalPages };
  });

  /** Whether the API returned any change log rows (before client filters). */
  readonly logHasAnyRows = computed(() => this.logRowsAll().length > 0);

  constructor() {
    effect(() => {
      const total = this.logTotalPages();
      const p = this.logPage();
      if (p > total) {
        this.logPage.set(total);
      }
    });
  }

  ngOnInit(): void {
    this.reload();
  }

  onPanelToggle(panel: OllamaCollapsiblePanelKey, ev: Event): void {
    const t = ev.target as HTMLDetailsElement | null;
    if (t?.tagName === 'DETAILS') {
      this.panelOpen[panel] = t.open;
    }
  }

  expandPanel(panel: OllamaCollapsiblePanelKey, ev: MouseEvent): void {
    ev.stopPropagation();
    ev.preventDefault();
    this.panelOpen[panel] = true;
  }

  collapsePanel(panel: OllamaCollapsiblePanelKey, ev: MouseEvent): void {
    ev.stopPropagation();
    ev.preventDefault();
    this.panelOpen[panel] = false;
  }

  reload(): void {
    this.loading = true;
    this.inlineError = null;
    this.loadFailed = false;
    this.ollama.getPreferences().subscribe({
      next: (p) => {
        this.prefs = p;
        this.writerInput = p.assignments.writerModel;
        this.criticInput = p.assignments.criticModel;
        this.agentInput = p.assignments.agentModel;
        this.worldBuildingInput = p.assignments.worldBuildingModel;
        this.preStateInput = p.assignments.preStateModel;
        this.postStateInput = p.assignments.postStateModel;
        this.loading = false;
      },
      error: () => {
        this.prefs = null;
        this.loadFailed = true;
        this.loading = false;
      }
    });
    this.ollama.getChangeLog(500).subscribe({
      next: (rows) => {
        this.logRowsAll.set(rows);
        this.logPage.set(1);
      },
      error: () => {
        this.logRowsAll.set([]);
      }
    });
  }

  onLogSearchInput(value: string): void {
    this.logSearch.set(value);
    this.logPage.set(1);
  }

  onLogRoleFilterChange(value: string): void {
    this.logRoleFilter.set(value === '' ? null : Number(value));
    this.logPage.set(1);
  }

  onLogSourceFilterChange(value: string): void {
    this.logSourceFilter.set(value);
    this.logPage.set(1);
  }

  clearLogFilters(): void {
    this.logSearch.set('');
    this.logRoleFilter.set(null);
    this.logSourceFilter.set('');
    this.logPage.set(1);
  }

  toggleLogSort(key: LogSortKey): void {
    if (this.logSortKey() === key) {
      this.logSortDir.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.logSortKey.set(key);
      this.logSortDir.set(key === 'when' ? 'desc' : 'asc');
    }
    this.logPage.set(1);
  }

  sortAriaSort(key: LogSortKey): 'none' | 'ascending' | 'descending' {
    if (this.logSortKey() !== key) {
      return 'none';
    }
    return this.logSortDir() === 'asc' ? 'ascending' : 'descending';
  }

  goLogPage(delta: number): void {
    const next = this.logPage() + delta;
    const total = this.logTotalPages();
    this.logPage.set(Math.min(Math.max(1, next), total));
  }

  setLogPage(p: number): void {
    const total = this.logTotalPages();
    this.logPage.set(Math.min(Math.max(1, Math.floor(p)), total));
  }

  save(): void {
    const body: OllamaModelAssignmentsPatch = {
      writerModel: this.writerInput.trim(),
      criticModel: this.criticInput.trim(),
      agentModel: this.agentInput.trim(),
      worldBuildingModel: this.worldBuildingInput.trim(),
      preStateModel: this.preStateInput.trim(),
      postStateModel: this.postStateInput.trim()
    };
    this.busy = true;
    this.inlineError = null;
    this.ollama.putPreferences(body).subscribe({
      next: () => {
        this.busy = false;
        this.reload();
      },
      error: () => {
        this.busy = false;
      }
    });
  }

  clearSlot(role: 'writer' | 'critic' | 'agent' | 'worldBuilding' | 'preState' | 'postState'): void {
    const body: OllamaModelAssignmentsPatch = {
      clearWriter: role === 'writer',
      clearCritic: role === 'critic',
      clearAgent: role === 'agent',
      clearWorldBuilding: role === 'worldBuilding',
      clearPreState: role === 'preState',
      clearPostState: role === 'postState'
    };
    this.busy = true;
    this.inlineError = null;
    this.ollama.putPreferences(body).subscribe({
      next: () => {
        this.busy = false;
        this.reload();
      },
      error: () => {
        this.busy = false;
      }
    });
  }

  runPull(): void {
    const m = this.pullModel.trim();
    if (!m) {
      this.inlineError = 'Enter a library model tag (e.g. llama3.2).';
      return;
    }
    this.busy = true;
    this.inlineError = null;
    this.pullProgress = { statusLine: 'Connecting…', percent: null };
    void this.ollama
      .pullLibraryModelStream(m, (line) => {
        this.pullProgress = this.pullProgressFromLine(line);
      })
      .then(
        () => {
          this.busy = false;
          this.pullProgress = null;
          this.pullModel = '';
          this.reload();
        },
        (err: unknown) => {
          this.busy = false;
          this.pullProgress = null;
          this.inlineError = err instanceof Error ? err.message : String(err);
        }
      );
  }

  /** Remove model files from the Ollama host (same as <code>ollama rm</code>). */
  confirmDelete(name: string): void {
    let msg = `Remove "${name}" from the Ollama server disk? This cannot be undone.`;
    if (this.isModelAssigned(name)) {
      msg +=
        ' This tag is still listed in one or more assignment fields above; clear or change those after deletion if needed.';
    }
    if (!globalThis.confirm(msg)) {
      return;
    }
    this.busy = true;
    this.inlineError = null;
    this.ollama.deleteModel(name).subscribe({
      next: () => {
        this.busy = false;
        this.reload();
      },
      error: () => {
        this.busy = false;
      }
    });
  }

  private isModelAssigned(name: string): boolean {
    if (!this.prefs) {
      return false;
    }
    const a = this.prefs.assignments;
    const slots = [
      a.writerModel,
      a.criticModel,
      a.agentModel,
      a.worldBuildingModel,
      a.preStateModel,
      a.postStateModel
    ];
    return slots.some((s) => String(s ?? '').trim() === name);
  }

  private pullProgressFromLine(line: OllamaPullStreamLine): { statusLine: string; percent: number | null } {
    const status = line.status?.trim() ?? '';
    if (line.total != null && line.total > 0 && line.completed != null) {
      const pct = Math.min(100, Math.round((line.completed / line.total) * 100));
      const label = status || 'Downloading';
      return { statusLine: `${label} — ${pct}%`, percent: pct };
    }
    if (status) {
      return { statusLine: status, percent: null };
    }
    return { statusLine: 'Working…', percent: null };
  }

  runImportUrl(): void {
    const u = this.importUrl.trim();
    const n = this.importName.trim();
    if (!u || !n) {
      this.inlineError = 'URL and model name are required.';
      return;
    }
    this.busy = true;
    this.inlineError = null;
    this.ollama.importFromUrl(u, n).subscribe({
      next: () => {
        this.busy = false;
        this.importUrl = '';
        this.importName = '';
        this.reload();
      },
      error: () => {
        this.busy = false;
      }
    });
  }

  pickInstalled(
    name: string,
    slot: 'writer' | 'critic' | 'agent' | 'worldBuilding' | 'preState' | 'postState'
  ): void {
    if (slot === 'writer') this.writerInput = name;
    else if (slot === 'critic') this.criticInput = name;
    else if (slot === 'agent') this.agentInput = name;
    else if (slot === 'worldBuilding') this.worldBuildingInput = name;
    else if (slot === 'preState') this.preStateInput = name;
    else this.postStateInput = name;
  }

  roleLabel(role: number): string {
    switch (role) {
      case 0:
        return 'Writer';
      case 1:
        return 'Critic';
      case 2:
        return 'Agent';
      case 3:
        return 'World-building';
      case 4:
        return 'Pre-state';
      case 5:
        return 'Post-state';
      default:
        return String(role);
    }
  }
}
