import { DatePipe } from '@angular/common';
import { Component, Input } from '@angular/core';
import { formatDuration, formatEventBadgeLabel, GenerationLogEntry } from '../../core/generation-progress-log';
import { LlmWorkingIndicatorComponent } from '../llm-working-indicator/llm-working-indicator.component';

@Component({
  selector: 'app-agent-progress-modal',
  standalone: true,
  imports: [DatePipe, LlmWorkingIndicatorComponent],
  templateUrl: './agent-progress-modal.component.html',
  styleUrl: './agent-progress-modal.component.scss'
})
export class AgentProgressModalComponent {
  @Input({ required: true }) title!: string;
  @Input() hint = '';
  @Input() documentLabel = 'Draft';
  @Input() documentParagraphs: string[] = [];
  @Input() changedParagraphIndices = new Set<number>();
  @Input() documentRevision = 0;
  @Input() changeSummary: string | null = null;
  @Input() logEntries: GenerationLogEntry[] = [];
  @Input() nowLabel: string | null = null;
  @Input() busy = false;
  @Input() showCancel = false;

  readonly formatEventBadgeLabel = formatEventBadgeLabel;
  readonly formatDuration = formatDuration;
}
