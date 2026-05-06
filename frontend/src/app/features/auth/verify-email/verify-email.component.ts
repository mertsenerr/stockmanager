import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../core/auth/auth.service';
import { AuthShellComponent } from '../auth-shell/auth-shell.component';

type State = 'pending' | 'success' | 'failure' | 'missing';

@Component({
  selector: 'app-verify-email',
  standalone: true,
  imports: [RouterLink, AuthShellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './verify-email.component.html',
})
export class VerifyEmailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);

  readonly state = signal<State>('pending');
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    const token = this.route.snapshot.queryParamMap.get('token');
    if (!token) {
      this.state.set('missing');
      return;
    }

    this.auth.verifyEmail({ token }).subscribe({
      next: () => this.state.set('success'),
      error: (err: HttpErrorResponse) => {
        this.errorMessage.set(err.error?.message ?? 'Doğrulama başarısız.');
        this.state.set('failure');
      },
    });
  }
}
