import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'app-skeleton-row',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (_ of rows; track $index) {
      <tr class="border-t border-dashed border-ink/20">
        @for (col of cols; track $index) {
          <td class="px-4 py-3">
            <span class="block h-3 rounded-md skel-shimmer" [style.width]="col" style="background: var(--color-surface-elevated); border: 1px solid rgba(26, 24, 20, 0.15);"></span>
          </td>
        }
      </tr>
    }
  `,
  styles: [`
    @keyframes shimmer {
      0% { opacity: 0.5; }
      50% { opacity: 1; }
      100% { opacity: 0.5; }
    }
    .skel-shimmer { animation: shimmer 1.4s ease-in-out infinite; }
  `],
})
export class SkeletonRowComponent {
  @Input() count = 5;
  @Input() widths: string[] = ['80%', '60%', '40%', '50%', '30%'];

  get rows() { return Array.from({ length: this.count }); }
  get cols() { return this.widths; }
}
