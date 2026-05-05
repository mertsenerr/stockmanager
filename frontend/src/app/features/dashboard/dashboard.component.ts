import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../core/auth/auth.service';
import { HealthService, HealthCheckResponse } from '../../core/services/health.service';
import { KullaniciService } from '../admin/kullanici.service';
import { KullaniciList } from '../admin/admin.models';
import { ToastService } from '../../shared/ui/toast/toast.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.component.html',
})
export class DashboardComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly health = inject(HealthService);
  private readonly kSvc = inject(KullaniciService);
  private readonly toast = inject(ToastService);

  readonly user = this.auth.currentUser;
  readonly healthState = signal<HealthCheckResponse | null>(null);
  readonly pendingUsers = signal<KullaniciList[]>([]);
  readonly approvingId = signal<string | null>(null);

  readonly canManageUsers = computed(() => {
    const r = this.user()?.rol;
    return r === 'Sistem' || r === 'SayimBaskani';
  });

  ngOnInit(): void {
    this.health.check().subscribe((res) => this.healthState.set(res));
    if (this.canManageUsers()) {
      this.loadPending();
    }
  }

  private loadPending(): void {
    this.kSvc.listPending().subscribe({
      next: (r) => this.pendingUsers.set(r),
      error: () => undefined,
    });
  }

  approve(u: KullaniciList): void {
    this.approvingId.set(u.id);
    this.kSvc.approve(u.id).subscribe({
      next: () => {
        this.toast.success(`${u.adSoyad} onaylandı.`);
        this.approvingId.set(null);
        this.loadPending();
      },
      error: (err: HttpErrorResponse) => {
        this.approvingId.set(null);
        this.toast.error(err.error?.message ?? 'Onay başarısız.');
      },
    });
  }
}
