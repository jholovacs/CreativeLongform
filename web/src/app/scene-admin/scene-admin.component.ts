import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Book, Chapter } from '../models/entities';
import { formatJsonPretty } from '../core/json-format';
import { SceneAdminRow, SceneAdminService, SceneAdminSortKey } from '../services/scene-admin.service';
import { UiIconComponent } from '../shared/ui-icon.component';

@Component({
  selector: 'app-scene-admin',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, UiIconComponent],
  templateUrl: './scene-admin.component.html',
  styleUrl: './scene-admin.component.scss'
})
export class SceneAdminComponent implements OnInit {
  private readonly sceneAdmin = inject(SceneAdminService);

  readonly pageSize = 15;

  books: Book[] = [];
  bookFilterId = '';
  searchInput = '';
  /** Applied search (set when user runs search). */
  searchApplied = '';
  sortKey: SceneAdminSortKey = 'story';

  pageIndex = 0;
  totalCount = 0;
  rows: SceneAdminRow[] = [];
  loading = false;
  errorMsg: string | null = null;

  modalOpen = false;
  modalMode: 'create' | 'edit' = 'edit';
  editingSceneId: string | null = null;
  createBookId = '';
  createChapterId = '';
  modalSaving = false;
  modalError: string | null = null;

  formTitle = '';
  formSynopsis = '';
  formInstructions = '';
  formNarrativePerspective = '';
  formNarrativeTense = '';
  formExpectedEndStateNotes = '';
  formBeginningStateJson = '';

  ngOnInit(): void {
    this.sceneAdmin.loadBooks().subscribe({
      next: (list) => {
        this.books = list;
      },
      error: () => {
        this.errorMsg = 'Could not load books.';
      }
    });
    this.load();
  }

  chaptersForCreate(): Chapter[] {
    const b = this.books.find((x) => x.id === this.createBookId);
    const ch = b?.chapters;
    if (!ch?.length) {
      return [];
    }
    return [...ch].sort((a, b) => a.order - b.order);
  }

  onCreateBookChange(): void {
    this.createChapterId = '';
    const first = this.chaptersForCreate()[0];
    if (first) {
      this.createChapterId = first.id;
    }
  }

  applySearch(): void {
    this.searchApplied = this.searchInput.trim();
    this.pageIndex = 0;
    this.load();
  }

  onBookFilterChange(): void {
    this.pageIndex = 0;
    this.load();
  }

  onSortChange(): void {
    this.pageIndex = 0;
    this.load();
  }

  load(): void {
    this.loading = true;
    this.errorMsg = null;
    const bookId = this.bookFilterId.trim() || null;
    this.sceneAdmin
      .listScenes({
        bookIdFilter: bookId,
        search: this.searchApplied,
        sortKey: this.sortKey,
        skip: this.pageIndex * this.pageSize,
        top: this.pageSize
      })
      .subscribe({
        next: (page) => {
          this.rows = page.items;
          this.totalCount = page.totalCount;
          this.loading = false;
        },
        error: () => {
          this.loading = false;
          this.errorMsg = 'Could not load scenes.';
        }
      });
  }

  totalPages(): number {
    if (this.totalCount <= 0) {
      return 0;
    }
    return Math.ceil(this.totalCount / this.pageSize);
  }

  rangeLabel(): string {
    if (this.totalCount === 0) {
      return '0 of 0';
    }
    const from = this.pageIndex * this.pageSize + 1;
    const to = Math.min((this.pageIndex + 1) * this.pageSize, this.totalCount);
    return `${from}–${to} of ${this.totalCount}`;
  }

  onFirstPage(): void {
    if (this.pageIndex > 0) {
      this.pageIndex = 0;
      this.load();
    }
  }

  onPrevPage(): void {
    if (this.pageIndex > 0) {
      this.pageIndex--;
      this.load();
    }
  }

  onNextPage(): void {
    if ((this.pageIndex + 1) * this.pageSize < this.totalCount) {
      this.pageIndex++;
      this.load();
    }
  }

  onLastPage(): void {
    const last = Math.max(0, this.totalPages() - 1);
    if (this.pageIndex !== last) {
      this.pageIndex = last;
      this.load();
    }
  }

  onFirstPageDisabled(): boolean {
    return this.loading || this.totalCount === 0 || this.pageIndex === 0;
  }

  onPrevPageDisabled(): boolean {
    return this.onFirstPageDisabled();
  }

  onNextPageDisabled(): boolean {
    return this.loading || this.totalCount === 0 || (this.pageIndex + 1) * this.pageSize >= this.totalCount;
  }

  onLastPageDisabled(): boolean {
    return this.loading || this.totalCount === 0 || this.pageIndex >= this.totalPages() - 1;
  }

  openCreate(): void {
    this.modalMode = 'create';
    this.editingSceneId = null;
    this.modalError = null;
    this.createBookId = this.books[0]?.id ?? '';
    this.createChapterId = '';
    if (this.createBookId) {
      const ch = this.chaptersForCreate()[0];
      this.createChapterId = ch?.id ?? '';
    }
    this.formTitle = '';
    this.modalOpen = true;
  }

  openEdit(row: SceneAdminRow): void {
    this.modalMode = 'edit';
    this.editingSceneId = row.id;
    this.modalError = null;
    this.formTitle = row.title ?? '';
    this.formSynopsis = row.synopsis ?? '';
    this.formInstructions = row.instructions ?? '';
    this.formNarrativePerspective = row.narrativePerspective ?? '';
    this.formNarrativeTense = row.narrativeTense ?? '';
    this.formExpectedEndStateNotes = row.expectedEndStateNotes ?? '';
    this.formBeginningStateJson = formatJsonPretty(row.beginningStateJson ?? '');
    this.modalOpen = true;
  }

  closeModal(): void {
    if (this.modalSaving) {
      return;
    }
    this.modalOpen = false;
  }

  saveModal(): void {
    if (this.modalMode === 'create') {
      if (!this.createChapterId.trim()) {
        this.modalError = 'Choose a chapter.';
        return;
      }
      this.modalSaving = true;
      this.modalError = null;
      const title = this.formTitle.trim() || undefined;
      this.sceneAdmin.createScene(this.createChapterId, { title }).subscribe({
        next: () => {
          this.modalSaving = false;
          this.modalOpen = false;
          this.load();
        },
        error: () => {
          this.modalSaving = false;
          this.modalError = 'Could not create scene.';
        }
      });
      return;
    }

    if (!this.editingSceneId) {
      return;
    }
    this.modalSaving = true;
    this.modalError = null;
    this.sceneAdmin
      .patchScene(this.editingSceneId, {
        title: this.formTitle,
        synopsis: this.formSynopsis,
        instructions: this.formInstructions,
        narrativePerspective: this.formNarrativePerspective.trim(),
        narrativeTense: this.formNarrativeTense.trim(),
        expectedEndStateNotes: this.formExpectedEndStateNotes.trim(),
        beginningStateJson: this.formBeginningStateJson.trim()
      })
      .subscribe({
        next: () => {
          this.modalSaving = false;
          this.modalOpen = false;
          this.load();
        },
        error: () => {
          this.modalSaving = false;
          this.modalError = 'Could not save scene.';
        }
      });
  }

  deleteRow(row: SceneAdminRow): void {
    const ok = confirm(`Delete scene "${row.title}"? This cannot be undone.`);
    if (!ok) {
      return;
    }
    this.loading = true;
    this.errorMsg = null;
    this.sceneAdmin.deleteScene(row.id).subscribe({
      next: () => {
        if (this.pageIndex > 0 && this.pageIndex * this.pageSize >= this.totalCount - 1) {
          this.pageIndex = Math.max(0, this.pageIndex - 1);
        }
        this.load();
      },
      error: () => {
        this.loading = false;
        this.errorMsg = 'Could not delete scene.';
      }
    });
  }

  bookTitle(row: SceneAdminRow): string {
    return row.chapter?.book?.title ?? '—';
  }

  chapterTitle(row: SceneAdminRow): string {
    return row.chapter?.title ?? '—';
  }

  synopsisPreview(row: SceneAdminRow): string {
    const s = row.synopsis?.trim() ?? '';
    if (s.length <= 120) {
      return s;
    }
    return s.slice(0, 117) + '…';
  }
}
