import { CommonModule } from '@angular/common';
import {
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild
} from '@angular/core';
import { TimelineEntry, TimelineEntryKindValue } from '../models/entities';

/** Precomputed item for the horizontal timeline visualization (position + stacking). */
export interface TimelineVizMarker {
  /** Source row (scene or world event). */
  entry: TimelineEntry;
  /** Horizontal position 0–100 within the viewport. */
  xPercent: number;
  /** Vertical lane index when overlapping markers. */
  stack: number;
  /** Display title (may differ from raw entry for truncation). */
  title: string;
  kind: TimelineEntryKindValue;
}

@Component({
  selector: 'app-timeline-viz-modal',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './timeline-viz-modal.component.html',
  styleUrl: './timeline-viz-modal.component.scss'
})
export class TimelineVizModalComponent implements OnChanges {
  @Input({ required: true }) open = false;
  @Input() entries: TimelineEntry[] = [];
  @Output() closed = new EventEmitter<void>();

  @ViewChild('viewport') private viewportRef?: ElementRef<HTMLDivElement>;

  /** CSS transform: translate + scale */
  zoom = 1;
  panX = 0;
  minKey = 0;
  maxKey = 1;
  /** Inner track width in px (wider when many beats for easier separation). */
  contentWidthPx = 2800;
  markers: TimelineVizMarker[] = [];

  private dragStartX = 0;
  private panStart = 0;
  private dragging = false;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['entries'] || changes['open']) {
      if (this.open) {
        this.zoom = 1;
        this.panX = 0;
        if (this.entries?.length) {
          this.contentWidthPx = Math.max(1600, Math.min(6400, this.entries.length * 52 + 1400));
          this.rebuildLayout();
        } else {
          this.markers = [];
          this.minKey = 0;
          this.maxKey = 1;
          this.contentWidthPx = 2800;
        }
      }
    }
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(ev: KeyboardEvent): void {
    if (!this.open) return;
    if (ev.key === 'Escape') {
      ev.preventDefault();
      this.close();
    }
  }

  close(): void {
    this.closed.emit();
  }

  onBackdropClick(ev: MouseEvent): void {
    if (ev.target === ev.currentTarget) this.close();
  }

  onWheel(ev: WheelEvent): void {
    if (!this.open) return;
    ev.preventDefault();
    const rect = (ev.currentTarget as HTMLElement).getBoundingClientRect();
    const mx = ev.clientX - rect.left;
    const oldZoom = this.zoom;
    const factor = ev.deltaY > 0 ? 0.92 : 1.08;
    const newZoom = Math.min(5, Math.max(0.2, this.zoom * factor));
    if (newZoom === this.zoom) return;
    // Keep point under cursor stable: pan adjusts so mx maps to same content position
    const contentX = (mx - this.panX) / oldZoom;
    this.panX = mx - contentX * newZoom;
    this.zoom = newZoom;
  }

  onPointerDown(ev: PointerEvent): void {
    if (!this.open) return;
    (ev.currentTarget as HTMLElement).setPointerCapture(ev.pointerId);
    this.dragging = true;
    this.dragStartX = ev.clientX;
    this.panStart = this.panX;
  }

  onPointerMove(ev: PointerEvent): void {
    if (!this.dragging) return;
    this.panX = this.panStart + (ev.clientX - this.dragStartX);
  }

  onPointerUp(ev: PointerEvent): void {
    this.dragging = false;
    try {
      (ev.currentTarget as HTMLElement).releasePointerCapture(ev.pointerId);
    } catch {
      /* ignore */
    }
  }

  zoomIn(): void {
    this.applyZoomAtCenter(1.15);
  }

  zoomOut(): void {
    this.applyZoomAtCenter(1 / 1.15);
  }

  resetView(): void {
    this.zoom = 1;
    this.panX = 0;
  }

  private applyZoomAtCenter(factor: number): void {
    const vp = this.viewportRef?.nativeElement;
    const w = vp?.clientWidth ?? 400;
    const mx = w / 2;
    const oldZoom = this.zoom;
    const newZoom = Math.min(5, Math.max(0.2, this.zoom * factor));
    const contentX = (mx - this.panX) / oldZoom;
    this.panX = mx - contentX * newZoom;
    this.zoom = newZoom;
  }

  trackTransform(): string {
    return `translateX(${this.panX}px) scale(${this.zoom})`;
  }

  private rebuildLayout(): void {
    const list = [...this.entries].filter((e) => e != null);
    if (list.length === 0) {
      this.markers = [];
      return;
    }
    const keys = list.map((e) => e.sortKey);
    this.minKey = Math.min(...keys);
    this.maxKey = Math.max(...keys);
    if (this.maxKey === this.minKey) {
      this.maxKey = this.minKey + 1;
    }
    const span = this.maxKey - this.minKey;

    const sorted = [...list].sort((a, b) => a.sortKey - b.sortKey);
    const bucketStacks = new Map<string, number>();

    this.markers = sorted.map((entry) => {
      const xPercent = ((entry.sortKey - this.minKey) / span) * 100;
      const bucket = Math.round(xPercent * 50) / 50;
      const key = String(bucket);
      const stack = bucketStacks.get(key) ?? 0;
      bucketStacks.set(key, stack + 1);
      const title = this.displayTitle(entry);
      return {
        entry,
        xPercent,
        stack,
        title,
        kind: entry.kind as TimelineEntryKindValue
      };
    });
  }

  private displayTitle(e: TimelineEntry): string {
    if (e.kind === 0 && e.scene?.title) return e.scene.title;
    const t = e.title?.trim();
    return t || 'Untitled';
  }

  /** Vertical position for dot (scene upper lane, event lower lane); stack offsets dense clusters. */
  dotTop(m: TimelineVizMarker): string {
    const basePct = m.kind === 0 ? 24 : 70;
    return `calc(${basePct}% + ${m.stack * 12}px)`;
  }

  /** Card sits just under the dot. */
  cardTop(m: TimelineVizMarker): string {
    const basePct = m.kind === 0 ? 24 : 70;
    return `calc(${basePct}% + ${m.stack * 12}px + 14px)`;
  }
}
