import { DestroyRef, effect, inject, signal, WritableSignal } from "@angular/core";
import { rxResource, takeUntilDestroyed } from "@angular/core/rxjs-interop";
import { Router } from "@angular/router";
import { finalize, from, lastValueFrom, Observable } from "rxjs";
import { FieldTree, submit, } from '@angular/forms/signals';
import { MessageService } from "primeng/api";

export interface FormActionsAdapter<Dto, Response = any> {
  create: (data: Dto) => Observable<Response>,
  update: (id: string, data: Dto) => Observable<Response>,
  delete: (id: string) => Observable<void | any>,
  get: (id: string) => Observable<any>
}

// 2. Parametr "form" przyjmuje teraz silny typ FieldTree bazujący na modelu Dto
export function createFormActions<Dto extends Record<string, any>, Response = any>(
  adapter: FormActionsAdapter<any, Response>,
  form: FieldTree<Dto>,
  modelSignal: WritableSignal<any>,
  idSignal: () => string | undefined,
  options?: {
    /** Tekst toastu sukcesu. Funkcja dostaje payload formularza + identyfikator (po update). */
    successMessage?: string | ((data: Dto, isUpdate: boolean) => string);
    errorMessage?: string;
    /** Pojedyncza ścieżka, tablica segmentów albo funkcja (np. po `input()` pracownika). */
    redirectUrl?: string | string[] | (() => string | string[]);
    /** Gdy podane — wywoływane po udanym zapisie ZAMIAST nawigacji (tryb drawer: zamknij + emit). */
    onSuccess?: () => void;
    /** Gdy podane — wywoływane przy anulowaniu ZAMIAST nawigacji (tryb drawer). */
    onCancel?: () => void;
  }
) {

  const destroyRef = inject(DestroyRef);
  const router = inject(Router);
  const messageService = inject(MessageService);

  const isPending = signal(false);
  const error = signal<any>(null);
  /** Po udanym `submit` pozwalamy guardom CanDeactivate puścić nawigację mimo `dirty()`. */
  const submittedSuccessfully = signal(false);

  effect(() => {
    const id = idSignal();

    if (id) {
      adapter.get(id).pipe(takeUntilDestroyed(destroyRef)).subscribe(data => {
        console.log(data);
        modelSignal.set(data);
      })
    }
  },)

  const handleSubmit = () => {
    // 1. Pobieramy aktualną wartość sygnału formy
    const currentForm = form();

    // 3. Sprawdzamy walidację (używając sygnałów .invalid() i .pending())

    // 4. Wywołujemy submit (przekazując SYGNAŁ form, a nie wywołanie form())
    // Funkcja submit od Google przyjmuje Signal<FieldTree>
    return submit(form, async () => {

      if (currentForm.invalid() || currentForm.pending()) {
        console.log('Formularz zawiera błędy - przerywam.');
        return;
      }

      isPending.set(true);
      console.log("Dane poprawne, wysyłam...");

      const id = idSignal();
      const formData = currentForm.value(); // pobieramy wartość z drzewa
      const data = { ...formData, id };

      const action$ = id ? adapter.update(id, data) : adapter.create(data);

      try {
        await lastValueFrom(action$);
        const detail =
          typeof options?.successMessage === 'function'
            ? options.successMessage(formData as Dto, !!id)
            : options?.successMessage || (id ? 'Zaktualizowano pomyślnie' : 'Utworzono pomyślnie');
        messageService.add({
          severity: 'success',
          summary: 'Sukces',
          detail,
        });
        submittedSuccessfully.set(true);
        if (options?.onSuccess) {
          options.onSuccess();
        } else {
          router.navigate(resolveRedirectCommands(options?.redirectUrl));
        }
      } catch (err: any) {
        messageService.add({
          severity: 'error',
          summary: 'Błąd',
          detail: options?.errorMessage || err?.message || 'Wystąpił błąd podczas zapisywania'
        });
        error.set(err);
        throw err;
      } finally {
        isPending.set(false);
      }
    });
  };




  const handleCancel = () => {
    submittedSuccessfully.set(true);
    if (options?.onCancel) {
      options.onCancel();
    } else {
      router.navigate(resolveRedirectCommands(options?.redirectUrl));
    }
  }

  /** Czy formularz ma niezapisane zmiany (do `CanDeactivate` guarda i `beforeunload`). */
  const hasUnsavedChanges = (): boolean => {
    if (submittedSuccessfully()) {
      return false;
    }
    try {
      const f = form();
      return !!f.dirty?.();
    } catch {
      return false;
    }
  };

  return { handleSubmit, handleCancel, isPending, error, hasUnsavedChanges };
}

function resolveRedirectCommands(
  raw: string | string[] | (() => string | string[]) | undefined
): any[] {
  if (raw === undefined) {
    return ['/admin/resources'];
  }
  const resolved = typeof raw === 'function' ? raw() : raw;
  if (Array.isArray(resolved)) {
    return resolved;
  }
  const s = resolved.trim();
  const path = s.startsWith('/') ? s : '/' + s;
  return [path];
}