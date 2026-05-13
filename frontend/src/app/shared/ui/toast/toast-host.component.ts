import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastService } from './toast.service';

@Component({
  selector: 'app-toast-host',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="toast-stack">
      @for (t of svc.toasts(); track t.id) {
        <div class="toast" [attr.data-kind]="t.kind">
          <span class="toast-icon" aria-hidden="true">
            {{ t.kind === 'success' ? '✓' : t.kind === 'error' ? '!' : 'i' }}
          </span>
          <span class="toast-message">{{ t.message }}</span>
          <button
            type="button"
            (click)="svc.dismiss(t.id)"
            class="toast-close focus-ring"
            aria-label="Kapat"
          >×</button>
        </div>
      }
    </div>
  `,
  styles: [`
    .toast-stack {
      position: fixed;
      top: 20px; right: 20px;
      z-index: 60;
      display: flex; flex-direction: column;
      gap: 10px;
      width: 320px;
      pointer-events: none;
    }
    .toast {
      pointer-events: auto;
      display: flex; align-items: flex-start; gap: 10px;
      padding: 12px 14px;
      border-radius: 10px;
      border: 1px solid;
      font-family: Inter, system-ui, sans-serif;
      font-size: 13px;
      line-height: 1.4;
    }
    .toast-icon {
      display: inline-flex; align-items: center; justify-content: center;
      width: 18px; height: 18px;
      border-radius: 999px;
      flex-shrink: 0;
      font-size: 11px;
      font-weight: 700;
      margin-top: 1px;
    }
    .toast-message { flex: 1; font-weight: 500; }
    .toast-close {
      background: transparent; border: none;
      font-size: 16px; line-height: 1; cursor: pointer;
      opacity: 0.6;
      padding: 0 2px;
    }
    .toast-close:hover { opacity: 1; }

    /* Light theme — soft tinted bg, dark text */
    .toast[data-kind='success'] {
      background: #ecfdf5;
      border-color: rgba(34, 197, 94, 0.35);
      color: #064e3b;
    }
    .toast[data-kind='success'] .toast-icon { background: rgba(34, 197, 94, 0.25); color: #047857; }
    .toast[data-kind='success'] .toast-close { color: #064e3b; }

    .toast[data-kind='error'] {
      background: #fef2f2;
      border-color: rgba(239, 68, 68, 0.35);
      color: #7f1d1d;
    }
    .toast[data-kind='error'] .toast-icon { background: rgba(239, 68, 68, 0.25); color: #b91c1c; }
    .toast[data-kind='error'] .toast-close { color: #7f1d1d; }

    .toast[data-kind='info'] {
      background: #fffbeb;
      border-color: rgba(245, 158, 11, 0.35);
      color: #78350f;
    }
    .toast[data-kind='info'] .toast-icon { background: rgba(245, 158, 11, 0.25); color: #b45309; }
    .toast[data-kind='info'] .toast-close { color: #78350f; }

    /* Dark theme — translucent surface bg, brand-tinted ring + text */
    :host-context([data-theme='dark']) .toast {
      backdrop-filter: blur(8px);
    }
    :host-context([data-theme='dark']) .toast[data-kind='success'] {
      background: rgba(34, 197, 94, 0.10);
      border-color: rgba(34, 197, 94, 0.30);
      color: #86efac;
    }
    :host-context([data-theme='dark']) .toast[data-kind='success'] .toast-icon {
      background: rgba(34, 197, 94, 0.20); color: #86efac;
    }
    :host-context([data-theme='dark']) .toast[data-kind='success'] .toast-close { color: #86efac; }

    :host-context([data-theme='dark']) .toast[data-kind='error'] {
      background: rgba(239, 68, 68, 0.10);
      border-color: rgba(239, 68, 68, 0.30);
      color: #fca5a5;
    }
    :host-context([data-theme='dark']) .toast[data-kind='error'] .toast-icon {
      background: rgba(239, 68, 68, 0.20); color: #fca5a5;
    }
    :host-context([data-theme='dark']) .toast[data-kind='error'] .toast-close { color: #fca5a5; }

    :host-context([data-theme='dark']) .toast[data-kind='info'] {
      background: rgba(245, 158, 11, 0.10);
      border-color: rgba(245, 158, 11, 0.30);
      color: #fcd34d;
    }
    :host-context([data-theme='dark']) .toast[data-kind='info'] .toast-icon {
      background: rgba(245, 158, 11, 0.20); color: #fcd34d;
    }
    :host-context([data-theme='dark']) .toast[data-kind='info'] .toast-close { color: #fcd34d; }
  `],
})
export class ToastHostComponent {
  readonly svc = inject(ToastService);
}
