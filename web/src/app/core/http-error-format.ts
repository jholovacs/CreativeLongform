import { HttpErrorResponse } from '@angular/common/http';
import { apiBaseUrl } from './api-config';

function stringifyBody(error: HttpErrorResponse): string {
  const b = error.error;
  if (b == null) return '(empty response body)';
  if (typeof b === 'string') return b.trim() || '(empty string body)';
  try {
    return JSON.stringify(b, null, 2);
  } catch {
    return String(b);
  }
}

const maxBodyChars = 100_000;

/**
 * Full text for the global error modal (includes request URL, status, and body/details).
 */
export function formatHttpFailureForDialog(err: unknown, method: string, url: string): string {
  const parts: string[] = [];
  parts.push(`Request: ${method.toUpperCase()} ${url}`);
  parts.push('');

  if (err instanceof HttpErrorResponse) {
    parts.push(`HTTP status: ${err.status}${err.statusText ? ` ${err.statusText}` : ''}`);
    parts.push('');

    if (err.status === 0) {
      const target = apiBaseUrl || '(same origin as this app)';
      parts.push(
        `Network error — the request did not complete (CORS, offline, wrong host, or connection refused). ` +
          `Expected API base: ${target}. Use http://localhost:4200 with ng serve, or the Docker UI on :8080 for same-origin API calls.`
      );
      parts.push('');
    }

    let detail = stringifyBody(err);
    if (detail.length > maxBodyChars) {
      detail = detail.slice(0, maxBodyChars) + '\n… (truncated)';
    }
    parts.push('Response / details:');
    parts.push(detail);

    if (err.message && !detail.includes(err.message)) {
      parts.push('');
      parts.push(`Angular: ${err.message}`);
    }
    return parts.join('\n');
  }

  if (err instanceof Error) {
    parts.push(err.message);
    return parts.join('\n');
  }

  parts.push(String(err));
  return parts.join('\n');
}
