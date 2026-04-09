import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Book } from '../models/entities';
import { ODataService } from '../services/odata.service';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent implements OnInit {
  private readonly odata = inject(ODataService);

  books: Book[] = [];
  error: string | null = null;
  loading = true;

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.error = null;
    this.loading = true;
    this.odata.getBooksWithScenes().subscribe({
      next: (res) => {
        this.books = res.value ?? [];
        this.loading = false;
      },
      error: (e) => {
        this.error = e?.message ?? 'Failed to load stories.';
        this.loading = false;
      }
    });
  }
}
