import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { RouterLink } from '@angular/router';

export type AuthVariant = 'login' | 'forgot' | 'reset' | 'register';

@Component({
  selector: 'app-auth-shell',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './auth-shell.component.html',
})
export class AuthShellComponent {
  @Input() variant: AuthVariant = 'login';

  /** Vertical offsets for the parallel ribbon lines stacked over the master wave path. */
  protected readonly waveOffsets = Array.from({ length: 28 }, (_, i) => i * 8 - 110);

  /**
   * Different master wave silhouette per variant. View Transitions API morphs
   * between them when the user navigates between auth pages.
   */
  private readonly wavePaths: Record<AuthVariant, string> = {
    login:    'M -40,560 C 80,460 150,180 240,140 S 290,360 340,310 C 410,260 430,140 470,110 S 560,420 660,580 C 740,650 850,500 940,450 S 1140,330 1300,400',
    register: 'M -40,420 C 120,360 240,560 380,480 S 500,180 600,210 C 720,240 800,560 920,520 S 1100,260 1300,320',
    forgot:   'M -40,500 C 100,420 220,260 360,300 S 520,520 640,460 C 760,400 860,180 980,220 S 1180,420 1300,360',
    reset:    'M -40,460 C 140,400 260,200 400,260 S 540,500 680,440 C 820,380 920,200 1040,260 S 1200,460 1300,400',
  };

  protected get masterPath(): string {
    return this.wavePaths[this.variant];
  }
}
