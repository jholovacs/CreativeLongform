import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { WorldService } from '../services/world.service';

@Component({
  selector: 'app-new-story',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './new-story.component.html',
  styleUrl: './new-story.component.scss'
})
export class NewStoryComponent {
  private readonly world = inject(WorldService);
  private readonly router = inject(Router);

  title = '';
  tone = '';
  styleNotes = '';
  synopsis = '';
  /** Extra text from paste or file — combined for bootstrap */
  sourceText = '';
  importedFileName: string | null = null;
  generateWorld = true;
  busy = false;
  error: string | null = null;

  onFileSelected(ev: Event): void {
    const input = ev.target as HTMLInputElement;
    const file = input.files?.[0];
    this.importedFileName = file?.name ?? null;
    if (!file) {
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      this.sourceText = String(reader.result ?? '');
    };
    reader.readAsText(file);
  }

  submit(): void {
    if (!this.title.trim()) {
      this.error = 'Title is required.';
      return;
    }
    this.busy = true;
    this.error = null;
    this.world
      .createBook({
        title: this.title.trim(),
        storyToneAndStyle: this.tone.trim() || undefined,
        contentStyleNotes: this.styleNotes.trim() || undefined,
        synopsis: this.synopsis.trim() || undefined
      })
      .subscribe({
        next: (created) => {
          if (!this.generateWorld) {
            this.busy = false;
            void this.router.navigate(['/book', created.id]);
            return;
          }
          const boot = {
            storyToneAndStyle: this.tone.trim() || undefined,
            contentStyleNotes: this.styleNotes.trim() || undefined,
            synopsis: this.synopsis.trim() || undefined,
            sourceText: this.sourceText.trim() || undefined
          };
          this.world.bootstrapWorld(created.id, boot).subscribe({
            next: (wb) => {
              this.busy = false;
              if (wb.suggestedLinks?.length) {
                sessionStorage.setItem(`clf.pendingLinks.${created.id}`, JSON.stringify(wb.suggestedLinks));
              }
              void this.router.navigate(['/book', created.id]);
            },
            error: () => {
              this.busy = false;
              void this.router.navigate(['/book', created.id]);
            }
          });
        },
        error: () => {
          this.busy = false;
        }
      });
  }
}
