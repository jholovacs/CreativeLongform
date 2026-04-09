import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./home/home.component').then((m) => m.HomeComponent) },
  {
    path: 'new-story',
    loadComponent: () => import('./new-story/new-story.component').then((m) => m.NewStoryComponent)
  },
  {
    path: 'book/:bookId',
    loadComponent: () => import('./book-world/book-world.component').then((m) => m.BookWorldComponent)
  },
  {
    path: 'scenes',
    loadComponent: () => import('./scene-workflow/scene-workflow.component').then((m) => m.SceneWorkflowComponent)
  }
];
