import { ChangeDetectionStrategy, Component, OnInit, computed, input, signal } from '@angular/core';

@Component({
  selector: 'app-hover-gif',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <img
      [src]="displaySrc()"
      [alt]="alt()"
      [class]="cssClass()"
      draggable="false"
      aria-hidden="true"
    />
  `,
})
export class HoverGifComponent implements OnInit {
  readonly src = input.required<string>();
  readonly alt = input('');
  readonly cssClass = input('');
  readonly hover = input(false);

  private readonly staticSrc = signal<string | null>(null);

  protected readonly displaySrc = computed(() =>
    this.hover() ? this.src() : (this.staticSrc() ?? this.src()),
  );

  ngOnInit(): void {
    const url = this.src();
    const probe = new Image();
    probe.onload = () => {
      try {
        const canvas = document.createElement('canvas');
        canvas.width = probe.naturalWidth || 64;
        canvas.height = probe.naturalHeight || 64;
        const ctx = canvas.getContext('2d');
        if (!ctx) return;
        ctx.drawImage(probe, 0, 0);
        this.staticSrc.set(canvas.toDataURL('image/png'));
      } catch {
        // Cross-origin tainting — keep animated src as fallback.
      }
    };
    probe.src = url;
  }
}
