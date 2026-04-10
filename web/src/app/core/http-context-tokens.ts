import { HttpContextToken } from '@angular/common/http';

/** When true, failed requests do not open the global error modal (e.g. optional secondary loads). */
export const SKIP_GLOBAL_ERROR_MODAL = new HttpContextToken<boolean>(() => false);
