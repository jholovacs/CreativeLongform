import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-llm-working-indicator',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './llm-working-indicator.component.html',
  styleUrl: './llm-working-indicator.component.scss'
})
export class LlmWorkingIndicatorComponent {
  @Input() visible = false;
  @Input() label = 'Model is working…';
}
