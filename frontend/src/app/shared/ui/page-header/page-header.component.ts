import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'app-page-header',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="flex items-center justify-between gap-4 mb-7 flex-wrap">
      <div>
        <h1 class="font-display font-bold text-[26px] tracking-tight leading-tight">{{ title }}</h1>
        @if (description) {
          <p class="text-sm text-ink-secondary mt-1.5 max-w-prose">{{ description }}</p>
        }
      </div>
      <ng-content select="[actions]" />
    </header>
  `,
})
export class PageHeaderComponent {
  @Input({ required: true }) title = '';
  @Input() description = '';
}
