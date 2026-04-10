import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { apiBaseUrl } from '../core/api-config';
import {
  OllamaModelAssignmentsPatch,
  OllamaModelChangeLogDto,
  OllamaModelsService,
  OllamaPreferencesResponse
} from '../services/ollama-models.service';
import { LlmWorkingIndicatorComponent } from '../shared/llm-working-indicator/llm-working-indicator.component';
import { UiIconComponent } from '../shared/ui-icon.component';
import { OLLAMA_MODELS_FIELD_HELP } from './ollama-models-field-help';

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
  importUrl = '';
  importName = '';

  logRows: OllamaModelChangeLogDto[] = [];

  ngOnInit(): void {
    this.reload();
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
    this.ollama.getChangeLog(80).subscribe({
      next: (rows) => {
        this.logRows = rows;
      },
      error: () => {
        /* optional */
      }
    });
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
    this.ollama.pullLibraryModel(m).subscribe({
      next: () => {
        this.busy = false;
        this.pullModel = '';
        this.reload();
      },
      error: () => {
        this.busy = false;
      }
    });
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
