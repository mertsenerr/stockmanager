import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  HostListener,
  Input,
  Output,
} from '@angular/core';
import { NgClass } from '@angular/common';

@Component({
  selector: 'app-modal',
  standalone: true,
  imports: [NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (open) {
      <div
        class="fixed inset-0 z-40 bg-ink/35 backdrop-blur-sm"
        (click)="onBackdrop()"
      ></div>
      <div
        role="dialog"
        aria-modal="true"
        class="fixed inset-0 z-50 flex items-center justify-center pointer-events-none"
        [ngClass]="size === 'full' ? 'p-2' : 'p-4'"
      >
        <div
          class="w-full pointer-events-auto bg-surface border-[1.5px] border-ink rounded-xl shadow-stamp"
          [ngClass]="{
            'max-w-md': size === 'sm',
            'max-w-lg': size === 'md',
            'max-w-2xl': size === 'lg',
            'max-w-[98vw] h-[96vh] flex flex-col': size === 'full'
          }"
        >
          <header class="flex items-center justify-between px-5 py-4 border-b-[1.5px] border-ink shrink-0">
            <h2 class="font-display font-bold text-base tracking-tight">{{ title }}</h2>
            <button
              type="button"
              (click)="onClose()"
              class="focus-ring text-ink-muted hover:text-coral text-2xl leading-none"
              aria-label="Kapat"
            >×</button>
          </header>
          <div
            [ngClass]="size === 'full' ? 'px-3 py-3 flex-1 overflow-y-auto min-h-0' : 'px-5 py-5'"
          >
            <ng-content />
          </div>
        </div>
      </div>
    }
  `,
})
export class ModalComponent {
  @Input({ required: true }) open = false;
  @Input() title = '';
  @Input() size: 'sm' | 'md' | 'lg' | 'full' = 'md';
  @Output() readonly closed = new EventEmitter<void>();

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.open) this.onClose();
  }

  onBackdrop(): void {
    this.onClose();
  }

  onClose(): void {
    this.closed.emit();
  }
}
