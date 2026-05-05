import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { RaporService } from '../rapor.service';
import { AKSIYON_LABELS, AuditLogItem } from '../rapor.models';

@Component({
  selector: 'app-audit',
  standalone: true,
  imports: [PageHeaderComponent, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './audit.component.html',
})
export class AuditComponent implements OnInit {
  private readonly svc = inject(RaporService);
  private readonly toast = inject(ToastService);

  readonly items = signal<AuditLogItem[]>([]);
  readonly total = signal(0);
  readonly skip = signal(0);
  readonly take = signal(50);
  readonly loading = signal(false);

  filterFrom = '';
  filterTo = '';
  filterAksiyon = '';

  readonly aksiyonOptions: { value: string; label: string }[] = [
    { value: '', label: 'Tüm aksiyonlar' },
    ...Object.entries(AKSIYON_LABELS).map(([value, label]) => ({ value, label })),
  ];

  readonly aksiyonLabel = (key: string) => AKSIYON_LABELS[key] ?? key;

  readonly hasNext = computed(() => this.skip() + this.take() < this.total());
  readonly hasPrev = computed(() => this.skip() > 0);

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);
    this.svc.audit({
      from: this.filterFrom || undefined,
      to: this.filterTo || undefined,
      aksiyon: this.filterAksiyon || undefined,
      skip: this.skip(),
      take: this.take(),
    }).subscribe({
      next: (r) => {
        this.items.set(r.items);
        this.total.set(r.total);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Audit log yüklenemedi.');
      },
    });
  }

  applyFilters(): void {
    this.skip.set(0);
    this.refresh();
  }

  next(): void {
    if (!this.hasNext()) return;
    this.skip.set(this.skip() + this.take());
    this.refresh();
  }

  prev(): void {
    if (!this.hasPrev()) return;
    this.skip.set(Math.max(0, this.skip() - this.take()));
    this.refresh();
  }

  formatDateTime(iso: string): string {
    return new Date(iso).toLocaleString('tr-TR');
  }

  severityClass(item: AuditLogItem): string {
    if (!item.basarili) return 'bg-accent-danger';
    if (item.aksiyon.includes('delete')) return 'bg-accent-warning';
    if (item.aksiyon.startsWith('auth.')) return 'bg-accent-info';
    return 'bg-text-muted';
  }
}
