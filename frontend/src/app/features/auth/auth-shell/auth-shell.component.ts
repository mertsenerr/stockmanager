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
}
