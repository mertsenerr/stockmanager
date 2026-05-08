import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { HealthService, HealthCheckResponse } from '../../core/services/health.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.component.html',
})
export class DashboardComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly health = inject(HealthService);

  readonly user = this.auth.currentUser;
  readonly healthState = signal<HealthCheckResponse | null>(null);

  ngOnInit(): void {
    this.health.check().subscribe((res) => this.healthState.set(res));
  }
}
