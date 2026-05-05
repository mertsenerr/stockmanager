import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { IncomingCallService } from './incoming-call.service';

@Component({
  selector: 'app-incoming-call-host',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (svc.incoming(); as ic) {
      <div class="fixed inset-0 z-[60] bg-ink/40 backdrop-blur-sm flex items-center justify-center p-4 pointer-events-auto"
           (click)="svc.dismiss()">
        <div class="bg-surface border-[1.5px] border-ink rounded-xl shadow-stamp w-full max-w-sm p-5"
             (click)="$event.stopPropagation()">
          <div class="flex items-center gap-3 mb-3">
            <div class="w-12 h-12 rounded-full border-[1.5px] border-ink flex items-center justify-center text-xl animate-pulse"
                 style="background: var(--color-mint);">
              📞
            </div>
            <div class="min-w-0">
              <p class="font-mono text-[10px] uppercase tracking-wider text-ink-muted">Gelen arama</p>
              <p class="font-display font-bold text-base tracking-tight truncate">{{ ic.baslatanAdi }}</p>
              <p class="text-[11px] text-ink-secondary">Sayım oturumunda görüşme başlattı</p>
            </div>
          </div>
          <p class="text-xs text-ink-muted mb-4">Katılırsan canlı sayım ekranına yönlendirileceksin.</p>
          <div class="flex items-center gap-2">
            <button type="button" (click)="svc.accept('video')"
                    class="btn btn-sm btn-primary flex-1">▶ Görüntülü katıl</button>
            <button type="button" (click)="svc.accept('audio')"
                    class="btn btn-sm btn-ghost flex-1">🎙 Sesli katıl</button>
            <button type="button" (click)="svc.dismiss()"
                    class="btn btn-sm shrink-0" style="background: var(--color-coral); color: #fff;">Reddet</button>
          </div>
        </div>
      </div>
    }
  `,
})
export class IncomingCallHostComponent {
  protected readonly svc = inject(IncomingCallService);
}
