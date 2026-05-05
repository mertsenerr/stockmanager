import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ModalComponent } from '../modal/modal.component';
import { ConfirmService } from './confirm.service';

@Component({
  selector: 'app-confirm-host',
  standalone: true,
  imports: [ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (svc.current(); as r) {
      <app-modal [open]="true" [title]="r.title" size="sm" (closed)="svc.resolve(false)">
        <p class="text-sm text-ink-secondary leading-relaxed">{{ r.message }}</p>
        <div class="flex items-center justify-end gap-3 mt-6">
          <button
            type="button"
            (click)="svc.resolve(false)"
            class="focus-ring btn btn-sm btn-ghost"
          >
            {{ r.cancelLabel ?? 'Vazgeç' }}
          </button>
          <button
            type="button"
            (click)="svc.resolve(true)"
            class="focus-ring btn btn-sm"
            [class.btn-danger]="r.danger"
            [class.btn-primary]="!r.danger"
          >
            {{ r.confirmLabel ?? 'Onayla' }}
          </button>
        </div>
      </app-modal>
    }
  `,
})
export class ConfirmHostComponent {
  readonly svc = inject(ConfirmService);
}
