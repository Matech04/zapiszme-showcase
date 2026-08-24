/**
 * Komponent implementujący ten interfejs informuje router/guard, czy formularz
 * ma niezapisane zmiany. Używane przez `dirtyFormGuard` (CanDeactivate).
 */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

export function isHasUnsavedChanges(value: unknown): value is HasUnsavedChanges {
  return (
    !!value
    && typeof value === 'object'
    && typeof (value as { hasUnsavedChanges?: unknown }).hasUnsavedChanges === 'function'
  );
}
