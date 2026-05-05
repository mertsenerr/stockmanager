import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastService } from './toast.service';

@Component({
  selector: 'app-toast-host',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="fixed top-5 right-5 z-[60] flex flex-col gap-2.5 w-80 pointer-events-none">
      @for (t of svc.toasts(); track t.id) {
        <div
          class="pointer-events-auto bg-surface border-[1.5px] border-ink rounded-lg px-3.5 py-3 flex items-start gap-2.5 text-sm shadow-stamp-sm"
          [style.background]="t.kind === 'success' ? 'var(--color-lime)' : t.kind === 'error' ? 'var(--color-coral)' : 'var(--color-butter)'"
          [style.color]="t.kind === 'error' ? '#fff' : 'var(--color-ink)'"
        >
          <span class="font-mono text-[10px] uppercase tracking-wider font-bold shrink-0 mt-0.5">
            {{ t.kind === 'success' ? '✓' : t.kind === 'error' ? '!' : 'i' }}
          </span>
          <span class="flex-1 leading-snug font-medium">{{ t.message }}</span>
          <button
            type="button"
            (click)="svc.dismiss(t.id)"
            class="focus-ring text-base leading-none opacity-70 hover:opacity-100"
            [style.color]="t.kind === 'error' ? '#fff' : 'var(--color-ink)'"
            aria-label="Kapat"
          >×</button>
        </div>
      }
    </div>
  `,
})
export class ToastHostComponent {
  readonly svc = inject(ToastService);
}
