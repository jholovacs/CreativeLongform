import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Book, Chapter } from '../models/entities';
import { ManuscriptResponse, ManuscriptService } from '../services/manuscript.service';
import { ODataService } from '../services/odata.service';
import { LlmWorkingIndicatorComponent } from '../shared/llm-working-indicator/llm-working-indicator.component';
import { UiIconComponent } from '../shared/ui-icon.component';

type Scope = 'book' | 'chapter';

@Component({
  selector: 'app-manuscript-workspace',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, UiIconComponent, LlmWorkingIndicatorComponent],
  templateUrl: './manuscript-workspace.component.html',
  styleUrl: './manuscript-workspace.component.scss'
})
export class ManuscriptWorkspaceComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly odata = inject(ODataService);
  private readonly manuscript = inject(ManuscriptService);

  bookId = '';
  book: Book | null = null;
  loading = true;
  error: string | null = null;
  busy = false;

  scope: Scope = 'book';
  chapterId: string | null = null;
  chapters: Chapter[] = [];

  draftText = '';
  computedAssembledText = '';
  storedSnapshot: string | null = null;

  ngOnInit(): void {
    this.bookId = this.route.snapshot.paramMap.get('bookId') ?? '';
    if (!this.bookId) {
      this.error = 'Missing book.';
      this.loading = false;
      return;
    }
    this.loadBook();
  }

  private loadBook(): void {
    this.loading = true;
    this.error = null;
    this.odata.getBook(this.bookId).subscribe({
      next: (res) => {
        const b = res.value?.[0];
        if (!b) {
          this.error = 'Book not found.';
          this.book = null;
          this.loading = false;
          return;
        }
        this.book = b;
        this.chapters = [...(b.chapters ?? [])].sort((a, b) => a.order - b.order);
        if (!this.chapterId && this.chapters.length > 0) {
          this.chapterId = this.chapters[0].id;
        }
        this.loading = false;
        void this.refreshManuscript({ silent: true });
      },
      error: () => {
        this.error = 'Could not load book.';
        this.loading = false;
      }
    });
  }

  onScopeChange(): void {
    void this.refreshManuscript();
  }

  onChapterChange(): void {
    if (this.scope === 'chapter') {
      void this.refreshManuscript();
    }
  }

  private async refreshManuscript(opts?: { silent?: boolean }): Promise<void> {
    if (!this.bookId) return;
    const silent = opts?.silent === true;
    this.error = null;
    if (!silent) this.busy = true;
    try {
      let res: ManuscriptResponse;
      if (this.scope === 'book') {
        res = await firstValueFrom(this.manuscript.getBookManuscript(this.bookId));
      } else {
        const cid = this.chapterId;
        if (!cid) {
          this.draftText = '';
          this.computedAssembledText = '';
          this.storedSnapshot = null;
          return;
        }
        res = await firstValueFrom(this.manuscript.getChapterManuscript(cid));
      }
      this.applyResponse(res);
    } catch {
      this.error = 'Could not load manuscript.';
    } finally {
      if (!silent) this.busy = false;
    }
  }

  private applyResponse(res: ManuscriptResponse): void {
    this.storedSnapshot = res.manuscriptText ?? null;
    this.computedAssembledText = res.computedAssembledText ?? '';
    this.draftText = res.effectiveText ?? '';
  }

  get hasStoredSnapshot(): boolean {
    return !!(this.storedSnapshot && this.storedSnapshot.trim().length > 0);
  }

  get statusHint(): string {
    if (this.hasStoredSnapshot) {
      return 'Editing the saved manuscript snapshot. Assemble from scenes overwrites this snapshot.';
    }
    return 'No saved snapshot yet — showing text assembled from scene manuscripts. Save to store a snapshot you can edit.';
  }

  useComputed(): void {
    this.draftText = this.computedAssembledText;
  }

  assembleFromScenes(): void {
    if (!this.bookId) return;
    if (this.scope === 'chapter' && !this.chapterId) return;
    this.busy = true;
    this.error = null;
    const req =
      this.scope === 'book'
        ? this.manuscript.assembleBook(this.bookId)
        : this.manuscript.assembleChapter(this.chapterId!);
    req.subscribe({
      next: (res) => {
        this.applyResponse(res);
        this.busy = false;
      },
      error: () => {
        this.error = 'Assemble failed.';
        this.busy = false;
      }
    });
  }

  save(): void {
    if (!this.bookId) return;
    if (this.scope === 'chapter' && !this.chapterId) return;
    this.busy = true;
    this.error = null;
    const req =
      this.scope === 'book'
        ? this.manuscript.patchBookManuscript(this.bookId, this.draftText)
        : this.manuscript.patchChapterManuscript(this.chapterId!, this.draftText);
    req.subscribe({
      next: (res) => {
        this.applyResponse(res);
        this.busy = false;
      },
      error: () => {
        this.error = 'Save failed.';
        this.busy = false;
      }
    });
  }

  clearPublished(): void {
    if (!this.bookId) return;
    if (this.scope === 'chapter' && !this.chapterId) return;
    if (!confirm('Clear the saved manuscript snapshot? The editor will show the computed assembly from scenes.')) return;
    this.busy = true;
    this.error = null;
    const req =
      this.scope === 'book'
        ? this.manuscript.patchBookManuscript(this.bookId, null)
        : this.manuscript.patchChapterManuscript(this.chapterId!, null);
    req.subscribe({
      next: (res) => {
        this.applyResponse(res);
        this.busy = false;
      },
      error: () => {
        this.error = 'Could not clear snapshot.';
        this.busy = false;
      }
    });
  }
}
