import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { ErrorDialogComponent } from './shared/error-dialog.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, ErrorDialogComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  title = 'Creative Longform';
}
