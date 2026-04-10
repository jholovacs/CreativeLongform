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
    path: 'scenes/:sceneId/draft',
    loadComponent: () => import('./scene-draft/scene-draft.component').then((m) => m.SceneDraftComponent)
  },
  {
    path: 'scenes',
    loadComponent: () => import('./scene-workflow/scene-workflow.component').then((m) => m.SceneWorkflowComponent)
  },
  {
    path: 'scene-admin',
    loadComponent: () => import('./scene-admin/scene-admin.component').then((m) => m.SceneAdminComponent)
  },
  {
    path: 'ollama-models',
    loadComponent: () => import('./ollama-models/ollama-models.component').then((m) => m.OllamaModelsComponent)
  }
];
