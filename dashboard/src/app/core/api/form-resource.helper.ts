import { inject, signal, Signal, effect } from '@angular/core';
import { Router } from '@angular/router';
import { rxResource } from '@angular/core/rxjs-interop';
import { of, finalize } from 'rxjs';
import { FormGroup } from '@angular/forms';
import { CrudClient } from './crud-resource.types';

export function useFormResource<T, TCreate, TUpdate>(
  client: CrudClient<T, TCreate, TUpdate>,
  id: Signal<string | undefined>,
  formGroup: FormGroup,
  redirectUrl: string = 'admin/resources'
) {
  const router = inject(Router);
  const isSaving = signal(false);
  const errorMessage = signal<string | null>(null);

  const resource = rxResource({
    params: () => id(),
    stream: (params) => {
      const currentId = params.params;
      if (!currentId) return of(undefined);
      return client.get(currentId);
    },
  });

  // Auto-fill form when data is loaded
  effect(() => {
    const data = resource.value();
    if (data) {
      formGroup.patchValue(data);
    }
  });

  const save = () => {
    if (formGroup.invalid || isSaving()) return;

    isSaving.set(true);
    errorMessage.set(null);

    const data = formGroup.getRawValue();
    const currentId = id();

    const request$ = currentId
      ? client.update(currentId, { ...data, id: currentId } as any)
      : client.create(data as any);

    request$.pipe(
      finalize(() => isSaving.set(false))
    ).subscribe({
      next: () => router.navigate([redirectUrl]),
      error: (err) => {
        console.error(err);
        errorMessage.set("Wystąpił błąd podczas zapisywania danych.");
      }
    });
  };

  const cancel = () => router.navigate([redirectUrl]);

  return {
    resource,
    save,
    cancel,
    isSaving,
    errorMessage,
    isLoading: resource.isLoading
  };
}
