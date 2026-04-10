import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

export type UiIconName =
  | 'arrow-left'
  | 'chart'
  | 'check'
  | 'clipboard'
  | 'copy'
  | 'chevron-down'
  | 'chevron-left'
  | 'chevron-right'
  | 'chevron-up'
  | 'chevrons-left'
  | 'chevrons-right'
  | 'trash'
  | 'plus'
  | 'pencil'
  | 'save'
  | 'search'
  | 'sparkles'
  | 'x';

@Component({
  selector: 'app-ui-icon',
  standalone: true,
  template: `
    <svg
      class="ui-icon"
      width="20"
      height="20"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      @switch (name) {
        @case ('arrow-left') {
          <path d="M19 12H5M12 19l-7-7 7-7" />
        }
        @case ('chart') {
          <g>
            <path d="M3 3v18h18" />
            <path d="M7 16V9M12 16v-5M17 16v-9" />
          </g>
        }
        @case ('check') {
          <path d="M20 6 9 17l-5-5" />
        }
        @case ('clipboard') {
          <g>
            <path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2" />
            <path d="M15 2H9a1 1 0 0 0-1 1v2h8V3a1 1 0 0 0-1-1z" />
          </g>
        }
        @case ('copy') {
          <g>
            <rect x="8" y="8" width="12" height="12" rx="2" ry="2" />
            <path d="M4 16V6a2 2 0 0 1 2-2h8" />
          </g>
        }
        @case ('chevron-down') {
          <path d="m6 9 6 6 6-6" />
        }
        @case ('chevron-left') {
          <path d="m15 18-6-6 6-6" />
        }
        @case ('chevron-right') {
          <path d="m9 18 6-6-6-6" />
        }
        @case ('chevron-up') {
          <path d="m18 15-6-6-6 6" />
        }
        @case ('chevrons-left') {
          <path d="m11 17-5-5 5-5M18 17l-5-5 5-5" />
        }
        @case ('chevrons-right') {
          <path d="m13 17 5-5-5-5M6 17l5-5-5-5" />
        }
        @case ('trash') {
          <path
            d="M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2m3 0v12a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6h14zM10 11v6M14 11v6"
          />
        }
        @case ('plus') {
          <path d="M12 5v14M5 12h14" />
        }
        @case ('pencil') {
          <path
            d="M12 20h9M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"
          />
        }
        @case ('save') {
          <path
            d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2zM17 21v-8H7v8M7 3v5h8"
          />
        }
        @case ('search') {
          <g>
            <circle cx="11" cy="11" r="8" />
            <path d="m21 21-4.3-4.3" />
          </g>
        }
        @case ('sparkles') {
          <path
            d="m12 3-1.9 5.8a2 2 0 0 1-1.3 1.3L3 12l5.8 1.9a2 2 0 0 1 1.3 1.3L12 21l1.9-5.8a2 2 0 0 1 1.3-1.3L21 12l-5.8-1.9a2 2 0 0 1-1.3-1.3L12 3zM5 3v4M19 17v4"
          />
        }
        @case ('x') {
          <path d="M18 6 6 18M6 6l12 12" />
        }
      }
    </svg>
  `,
  styles: [
    `
      :host {
        display: inline-flex;
        flex-shrink: 0;
        vertical-align: middle;
        color: inherit;
      }
      .ui-icon {
        display: block;
      }
    `
  ],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class UiIconComponent {
  @Input({ required: true }) name!: UiIconName;
}
