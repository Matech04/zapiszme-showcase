import { Component, computed, input, model, signal } from '@angular/core';
import { FormValueControl, ValidationError } from '@angular/forms/signals';
import { InputTextModule } from 'primeng/inputtext'; // Zmieniono na Module
import { Textarea } from 'primeng/textarea';

@Component({
  selector: 'app-form-field',
  standalone: true,
  imports: [InputTextModule, Textarea],
  template: `
<div class="flex flex-col gap-2 w-full">
    <label [for]="id()" class="text-sm font-bold text-surface-700 dark:text-surface-300 uppercase tracking-wider px-1">
      {{ label() }}
    </label>

    @if (multiline()) {
      <textarea
        pTextarea
        [id]="id()"
        [attr.data-testid]="testId()"
        [placeholder]="placeholder() || ''"
        [value]="value()"
        (input)="value.set($any($event.target).value)"
        (blur)="markAsTouched()"
        [invalid]="touched() && errors().length > 0"
        [rows]="rows()"
        class="w-full py-3 px-4 rounded-xl transition-all duration-200"
      ></textarea>
    } @else {
      <div class="relative">
        <input
          pInputText
          [id]="id()"
          [attr.data-testid]="testId()"
          [placeholder]="placeholder() || ''"
          [value]="value()"
          (input)="value.set($any($event.target).value)"
          (blur)="markAsTouched()"
          [invalid]="touched() && errors().length > 0"
          [type]="effectiveType()"
          class="w-full py-3 px-4 rounded-xl transition-all duration-200"
          [class.pr-12]="isPassword()"
        />
        @if (isPassword()) {
          <button
            type="button"
            [attr.data-testid]="testId() ? testId() + '-reveal' : null"
            (click)="toggleReveal()"
            [attr.aria-label]="revealed() ? 'Ukryj hasło' : 'Pokaż hasło'"
            [attr.aria-pressed]="revealed()"
            class="absolute right-3 top-1/2 -translate-y-1/2 grid size-8 place-items-center rounded-lg text-surface-400 hover:text-surface-700 dark:hover:text-surface-300 transition-colors"
          >
            <i [class]="revealed() ? 'pi pi-eye-slash' : 'pi pi-eye'" aria-hidden="true"></i>
          </button>
        }
      </div>
    }

    @if (touched() && errors().length > 0) {
    <div class="flex flex-col gap-1 px-1">
      @for (error of errors(); track error) {
        <small class="text-red-500 dark:text-red-400 text-xs font-medium animate-fadein">
          {{ $any(error).message || 'Pole jest niepoprawne' }}
        </small>
      }
    </div>
    }

    <!--
      Podpowiedź jest TRWAŁA, nie w placeholderze: placeholder znika, gdy tylko zaczniesz pisać,
      a przy dłuższym tekście na wąskim ekranie i tak jest ucinany w pół słowa (wymagania hasła
      kończyły się na „wielka i mał"). Zostaje widoczna także przy błędzie — właśnie wtedy jest
      potrzebna najbardziej.
    -->
    @if (hint()) {
      <small class="text-surface-500 dark:text-surface-400 text-xs leading-relaxed px-1">
        {{ hint() }}
      </small>
    }
</div>
  `
})
export class FormFieldComponent implements FormValueControl<string | number | null> {
  readonly value = model<string | number | null>(null);
  readonly errors = input<readonly ValidationError.WithOptionalFieldTree[]>([]);
  readonly touched = model<boolean>(false);

  label = input<string>();
  id = input.required<string>();
  testId = input<string>();
  placeholder = input<string>();
  type = input<string>('text');
  multiline = input<boolean>(false);
  rows = input<number>(3);

  /** Trwała podpowiedź pod polem (np. wymagania hasła) — nie znika przy pisaniu, inaczej niż placeholder. */
  hint = input<string>();

  /**
   * Podgląd hasła. Pola typu `password` wpisuje się na ślepo, a w rejestracji dwa razy — i trzeba
   * je odtworzyć przy logowaniu kilka minut później. Przełącznik dotyczy tylko `type="password"`,
   * więc reszta formularzy jest nietknięta.
   */
  protected readonly revealed = signal(false);

  protected readonly isPassword = computed(() => this.type() === 'password');

  protected readonly effectiveType = computed(() =>
    this.isPassword() && this.revealed() ? 'text' : this.type(),
  );

  protected toggleReveal(): void {
    this.revealed.update((v) => !v);
  }

  markAsTouched() {
    this.touched.set(true);
  }
}