import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ModalComponent } from '../modal/modal.component';
import { StepUpService } from './step-up.service';

@Component({
  selector: 'app-step-up-host',
  standalone: true,
  imports: [ReactiveFormsModule, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (svc.current(); as r) {
      <app-modal [open]="true" [title]="r.title" size="sm" (closed)="svc.cancel()">
        <form [formGroup]="form" (ngSubmit)="onSubmit()" class="space-y-4" novalidate>
          <p class="text-sm text-ink-secondary leading-relaxed">{{ r.message }}</p>

          <div class="space-y-1.5">
            <label class="text-[11px] font-semibold uppercase tracking-wide text-ink-secondary"
                   for="step-up-password">
              Mevcut parolan
            </label>
            <input
              id="step-up-password"
              type="password"
              autocomplete="current-password"
              formControlName="currentPassword"
              class="field-input w-full"
              [class.is-error]="form.controls.currentPassword.touched && form.controls.currentPassword.invalid"
              autofocus
            />
            @if (form.controls.currentPassword.touched && form.controls.currentPassword.errors?.['required']) {
              <p class="text-[11px] text-coral">Parola zorunlu.</p>
            }
          </div>

          @if ((r.twoFactorMethods?.length ?? 0) > 0) {
            <div class="space-y-1.5">
              <label class="text-[11px] font-semibold uppercase tracking-wide text-ink-secondary">
                İkinci faktör
              </label>
              <div class="inline-flex flex-wrap gap-1 p-[3px] rounded-lg border border-ink/10 bg-surface-elevated">
                @for (m of r.twoFactorMethods!; track m.value) {
                  <button
                    type="button"
                    class="px-3 py-1.5 rounded-md text-xs font-semibold transition-colors"
                    [class.bg-surface]="form.controls.twoFactorMethod.value === m.value"
                    [class.text-ink]="form.controls.twoFactorMethod.value === m.value"
                    [class.text-ink-secondary]="form.controls.twoFactorMethod.value !== m.value"
                    (click)="selectMethod(m.value)"
                  >
                    {{ m.label }}
                  </button>
                }
              </div>
              <input
                type="text"
                inputmode="numeric"
                autocomplete="one-time-code"
                formControlName="twoFactorCode"
                class="field-input w-full"
                [placeholder]="placeholder()"
                [class.is-error]="form.controls.twoFactorCode.touched && form.controls.twoFactorCode.invalid"
              />
              @if (form.controls.twoFactorCode.touched && form.controls.twoFactorCode.errors?.['required']) {
                <p class="text-[11px] text-coral">Kod zorunlu.</p>
              }
            </div>
          }

          <div class="flex items-center justify-end gap-3 pt-2">
            <button
              type="button"
              (click)="svc.cancel()"
              class="focus-ring btn btn-sm btn-ghost"
            >
              {{ r.cancelLabel ?? 'Vazgeç' }}
            </button>
            <button
              type="submit"
              [disabled]="form.invalid"
              class="focus-ring btn btn-sm"
              [class.btn-danger]="r.danger"
              [class.btn-primary]="!r.danger"
            >
              {{ r.confirmLabel ?? 'Onayla' }}
            </button>
          </div>
        </form>
      </app-modal>
    }
  `,
})
export class StepUpHostComponent {
  readonly svc = inject(StepUpService);
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    currentPassword: ['', [Validators.required]],
    twoFactorMethod: this.fb.nonNullable.control<'totp' | 'email' | 'recovery'>('totp'),
    twoFactorCode: [''],
  });

  // Dynamic required toggle on the 2FA code based on whether any methods were
  // offered by the requester.
  readonly hasMethods = computed(() => (this.svc.current()?.twoFactorMethods?.length ?? 0) > 0);

  constructor() {
    // Reset the form whenever the service opens a fresh step-up dialog. Using
    // a signal effect so cleanup is automatic on component destroy.
    effect(() => {
      const cur = this.svc.current();
      this.form.reset({ currentPassword: '', twoFactorMethod: 'totp', twoFactorCode: '' });
      const codeCtrl = this.form.controls.twoFactorCode;
      if ((cur?.twoFactorMethods?.length ?? 0) > 0) {
        codeCtrl.addValidators(Validators.required);
        // Seed the picker with the first available method.
        this.form.controls.twoFactorMethod.setValue(cur!.twoFactorMethods![0].value);
        const m = cur!.twoFactorMethods![0].value;
        this.placeholder.set(m === 'recovery' ? 'XXXX-XXXX-XXXX' : '6 haneli kod');
      } else {
        codeCtrl.clearValidators();
      }
      codeCtrl.updateValueAndValidity();
    });
  }

  readonly placeholder = signal<string>('6 haneli kod');

  selectMethod(m: 'totp' | 'email' | 'recovery'): void {
    this.form.controls.twoFactorMethod.setValue(m);
    this.form.controls.twoFactorCode.setValue('');
    this.placeholder.set(m === 'recovery' ? 'XXXX-XXXX-XXXX' : '6 haneli kod');
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    if (this.hasMethods()) {
      this.svc.submit({
        currentPassword: v.currentPassword,
        twoFactorMethod: v.twoFactorMethod,
        twoFactorCode: v.twoFactorCode,
      });
    } else {
      this.svc.submit({ currentPassword: v.currentPassword });
    }
  }
}
