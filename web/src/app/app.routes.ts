import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./scene-workflow/scene-workflow.component').then((m) => m.SceneWorkflowComponent) },
  {
    path: 'book/:bookId',
    loadComponent: () => import('./book-world/book-world.component').then((m) => m.BookWorldComponent)
  }
];
