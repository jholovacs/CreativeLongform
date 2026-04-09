import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Book, Scene, WorldElement } from '../models/entities';
import { GenerationService } from '../services/generation.service';
import { ODataService } from '../services/odata.service';
import { WorldService } from '../services/world.service';

@Component({
  selector: 'app-scene-workflow',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './scene-workflow.component.html',
  styleUrl: './scene-workflow.component.scss'
})
export class SceneWorkflowComponent implements OnInit {
  private readonly odata = inject(ODataService);
  private readonly generation = inject(GenerationService);
  private readonly world = inject(WorldService);

  books: Book[] = [];
  selectedSceneId: string | null = null;
  worldElements: WorldElement[] = [];
  selectedWorldIds = new Set<string>();
  logLines: string[] = [];
  error: string | null = null;
  busy = false;
  worldBusy = false;

  ngOnInit(): void {
    this.loadBooks();
  }

  loadBooks(): void {
    this.error = null;
    this.odata.getBooksWithScenes().subscribe({
      next: (res) => {
        this.books = res.value ?? [];
        const firstScene = this.books[0]?.chapters?.[0]?.scenes?.[0];
        this.selectedSceneId = firstScene?.id ?? null;
        this.loadSceneWorldData();
      },
      error: (e) => {
        this.error = e?.message ?? 'Failed to load books (is the API running?)';
      }
    });
  }

  selectScene(ev: Event): void {
    const value = (ev.target as HTMLSelectElement).value;
    this.selectedSceneId = value || null;
    this.loadSceneWorldData();
  }

  get selectedBookId(): string | null {
    return this.getBookIdForScene(this.selectedSceneId);
  }

  private getBookIdForScene(sceneId: string | null): string | null {
    if (!sceneId) return null;
    for (const b of this.books) {
      for (const ch of b.chapters ?? []) {
        for (const s of ch.scenes ?? []) {
          if (s.id === sceneId) return b.id;
        }
      }
    }
    return null;
  }

  loadSceneWorldData(): void {
    const bookId = this.selectedBookId;
    if (!bookId || !this.selectedSceneId) {
      this.worldElements = [];
      this.selectedWorldIds = new Set();
      return;
    }
    this.worldBusy = true;
    this.world.getWorldElements(bookId).subscribe({
      next: (res) => {
        this.worldElements = res.value ?? [];
        this.worldBusy = false;
      },
      error: () => {
        this.worldBusy = false;
      }
    });
    this.world.getSceneWorldElementIds(this.selectedSceneId).subscribe({
      next: (res) => {
        const ids = res.value?.map((r) => r.worldElementId) ?? [];
        this.selectedWorldIds = new Set(ids);
      },
      error: () => {
        this.selectedWorldIds = new Set();
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
  }

  saveSceneWorld(): void {
    if (!this.selectedSceneId) return;
    this.worldBusy = true;
    this.error = null;
    this.world.putSceneWorldElements(this.selectedSceneId, [...this.selectedWorldIds]).subscribe({
      next: () => {
        this.worldBusy = false;
      },
      error: (e) => {
        this.error = e?.message ?? 'Failed to save world links';
        this.worldBusy = false;
      }
    });
  }

  scenesForUi(): { scene: Scene; label: string }[] {
    const rows: { scene: Scene; label: string }[] = [];
    for (const b of this.books) {
      for (const ch of b.chapters ?? []) {
        for (const s of ch.scenes ?? []) {
          rows.push({ scene: s, label: `${b.title} / ${ch.title} / ${s.title}` });
        }
      }
    }
    return rows;
  }

  startGeneration(): void {
    if (!this.selectedSceneId) {
      this.error = 'Select a scene first.';
      return;
    }
    this.busy = true;
    this.error = null;
    this.logLines = ['Starting generation…'];
    this.generation.startGeneration(this.selectedSceneId).subscribe({
      next: (res) => {
        const runId = res.id;
        this.logLines.push(`Run id: ${runId}`);
        this.generation.connectToRun(runId, {
          onStep: (p) =>
            this.logLines.push(`[${p.step ?? 'step'}] ${p.detail ?? ''}`.trim()),
          onAgentEdit: (p) =>
            this.logLines.push(`[agent] ${p.detail ?? ''}`.trim()),
          onRepair: (p) =>
            this.logLines.push(`[repair] ${p.detail ?? ''}`.trim()),
          onFinished: (p) => {
            this.logLines.push(`[finished] ${p.detail ?? 'ok'}`.trim());
            this.busy = false;
            this.loadBooks();
          }
        });
      },
      error: (e) => {
        this.error = e?.error ?? e?.message ?? 'Generation failed';
        this.busy = false;
      }
    });
  }
}
