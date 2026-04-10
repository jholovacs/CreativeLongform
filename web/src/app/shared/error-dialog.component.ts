import { Component, HostListener, inject, signal } from '@angular/core';
import { ErrorDialogService } from '../services/error-dialog.service';

@Component({
  selector: 'app-error-dialog',
  standalone: true,
  template: `
    @if (dialog.open()) {
      <div class="error-dialog-backdrop" (click)="dialog.dismiss()" aria-hidden="true"></div>
      <div
        class="error-dialog"
        role="alertdialog"
        aria-modal="true"
        [attr.aria-labelledby]="titleId"
      >
        <header class="error-dialog-header">
          <h2 [id]="titleId">Request failed</h2>
          <button type="button" class="error-dialog-close" (click)="dialog.dismiss()" aria-label="Close">
            ×
          </button>
        </header>
        <pre class="error-dialog-body">{{ dialog.message() }}</pre>
        <footer class="error-dialog-footer">
          <button type="button" class="btn-copy" (click)="copy()" [disabled]="copyBusy()">
            {{ copyHint() }}
          </button>
          <button type="button" class="btn-close" (click)="dialog.dismiss()">Close</button>
        </footer>
      </div>
    }
  `,
  styles: [
    `
      .error-dialog-backdrop {
        position: fixed;
        inset: 0;
        z-index: 1000;
        background: rgba(0, 0, 0, 0.45);
      }
      .error-dialog {
        position: fixed;
        z-index: 1001;
        left: 50%;
        top: 50%;
        transform: translate(-50%, -50%);
        width: min(42rem, calc(100vw - 2rem));
        max-height: min(85vh, 40rem);
        display: flex;
        flex-direction: column;
        background: var(--surface);
        color: var(--text);
        border: 1px solid var(--border);
        border-radius: 10px;
        box-shadow: 0 12px 40px rgba(0, 0, 0, 0.2);
      }
      .error-dialog-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 0.75rem;
        padding: 0.75rem 1rem;
        border-bottom: 1px solid var(--border);
        flex-shrink: 0;
      }
      .error-dialog-header h2 {
        margin: 0;
        font-size: 1.1rem;
        font-weight: 600;
      }
      .error-dialog-close {
        border: none;
        background: transparent;
        font-size: 1.5rem;
        line-height: 1;
        padding: 0.15rem 0.35rem;
        cursor: pointer;
        color: var(--muted);
        border-radius: 4px;
      }
      .error-dialog-close:hover {
        color: var(--text);
        background: var(--border);
      }
      .error-dialog-body {
        margin: 0;
        padding: 0.75rem 1rem;
        flex: 1;
        min-height: 0;
        overflow: auto;
        font-family: ui-monospace, 'Cascadia Code', 'Consolas', monospace;
        font-size: 0.8rem;
        line-height: 1.45;
        white-space: pre-wrap;
        word-break: break-word;
      }
      .error-dialog-footer {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
        justify-content: flex-end;
        padding: 0.75rem 1rem;
        border-top: 1px solid var(--border);
        flex-shrink: 0;
      }
      .btn-copy,
      .btn-close {
        font: inherit;
        padding: 0.4rem 0.85rem;
        border-radius: 6px;
        cursor: pointer;
      }
      .btn-copy {
        border: 1px solid var(--border);
        background: var(--input-bg);
        color: var(--text);
      }
      .btn-copy:hover:not(:disabled) {
        border-color: var(--accent);
      }
      .btn-copy:disabled {
        opacity: 0.7;
        cursor: wait;
      }
      .btn-close {
        border: none;
        background: var(--accent);
        color: #fff;
        font-weight: 500;
      }
      .btn-close:hover {
        filter: brightness(1.06);
      }
    `
  ]
})
export class ErrorDialogComponent {
  readonly dialog = inject(ErrorDialogService);
  readonly titleId = 'app-error-dialog-title';
  readonly copyBusy = signal(false);
  readonly copyHint = signal('Copy to clipboard');

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.dialog.open()) this.dialog.dismiss();
  }

  async copy(): Promise<void> {
    const text = this.dialog.message();
    if (!text || !globalThis.navigator?.clipboard) return;
    this.copyBusy.set(true);
    try {
      await globalThis.navigator.clipboard.writeText(text);
      this.copyHint.set('Copied');
      setTimeout(() => {
        this.copyHint.set('Copy to clipboard');
        this.copyBusy.set(false);
      }, 1800);
    } catch {
      this.copyBusy.set(false);
    }
  }
}
