/** OData JSON may use `value` or `Value` for the entity set payload. */
export function odataRows<T>(res: unknown): T[] {
  if (res && typeof res === 'object') {
    const o = res as Record<string, unknown>;
    const v = o['value'] ?? o['Value'];
    if (Array.isArray(v)) {
      return v as T[];
    }
  }
  return [];
}

/** Total row count when the request used `$count=true`. */
export function odataCount(res: unknown): number | undefined {
  if (res && typeof res === 'object') {
    const o = res as Record<string, unknown>;
    const c = o['@odata.count'];
    if (typeof c === 'number' && Number.isFinite(c)) {
      return c;
    }
  }
  return undefined;
}
