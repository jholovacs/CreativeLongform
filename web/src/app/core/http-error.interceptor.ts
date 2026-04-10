import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ErrorDialogService } from '../services/error-dialog.service';
import { SKIP_GLOBAL_ERROR_MODAL } from './http-context-tokens';
import { formatHttpFailureForDialog } from './http-error-format';

export const httpErrorInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.context.get(SKIP_GLOBAL_ERROR_MODAL)) {
    return next(req);
  }

  const dialog = inject(ErrorDialogService);
  return next(req).pipe(
    catchError((err: unknown) => {
      const url = err instanceof HttpErrorResponse && err.url ? err.url : req.urlWithParams;
      const text = formatHttpFailureForDialog(err, req.method, url);
      dialog.show(text);
      return throwError(() => err);
    })
  );
};
