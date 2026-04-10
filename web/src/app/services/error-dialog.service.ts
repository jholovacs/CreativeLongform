import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ErrorDialogService {
  /** Modal visibility. */
  readonly open = signal(false);
  /** Full error text shown in the modal (fixed-width / pre-wrap). */
  readonly message = signal('');

  show(text: string): void {
    this.message.set(text);
    this.open.set(true);
  }

  dismiss(): void {
    this.open.set(false);
  }
}
