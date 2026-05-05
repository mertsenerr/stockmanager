import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { IncomingCallService } from '../../../core/realtime/incoming-call.service';
import { CallService } from '../live/call.service';
import { HttpErrorResponse } from '@angular/common/http';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { ModalComponent } from '../../../shared/ui/modal/modal.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ConfirmService } from '../../../shared/ui/confirm/confirm.service';
import { AuthService } from '../../../core/auth/auth.service';
import { OturumService } from '../oturum.service';
import {
  OTURUM_DURUM_COLOR,
  OTURUM_DURUM_LABELS,
  OturumDetail,
  OturumDurum,
  URUN_DURUM_LABELS,
  UrunDurum,
} from '../sayim.models';
import { ExcelUploadComponent } from './excel-upload.component';
import { OturumLiveComponent } from '../live/oturum-live.component';

@Component({
  selector: 'app-oturum-detail',
  standalone: true,
  imports: [PageHeaderComponent, ExcelUploadComponent, RouterLink, ModalComponent, OturumLiveComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './oturum-detail.component.html',
})
export class OturumDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly svc = inject(OturumService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly auth = inject(AuthService);
  private readonly incomingCalls = inject(IncomingCallService);
  private readonly callSvc = inject(CallService);

  constructor() {
    // Davet kabulü pendingCall signal'ı set eder — kendi oturumu ise:
    //   - Modal kapalıysa: aç + autoCallMode input'u ile live komponent ngOnInit'te autostart tetikler.
    //   - Modal açıksa: live komponent zaten yaratılmış, ngOnInit tekrar çalışmaz; CallService'i direkt çağır.
    effect(() => {
      const pending = this.incomingCalls.pendingCall();
      const o = this.oturum();
      if (!pending || !o || pending.oturumId !== o.id) return;

      if (this.liveOpen()) {
        // Modal zaten açık — direkt CallService'i tetikle.
        this.callSvc.start(o.id, pending.mode).catch(() => undefined);
      } else {
        this.pendingCallMode.set(pending.mode);
        this.liveOpen.set(true);
      }
      this.incomingCalls.consumePendingCall();
    });
  }

  readonly oturum = signal<OturumDetail | null>(null);
  readonly loading = signal(true);
  readonly uploading = signal(false);
  readonly liveOpen = signal(false);
  readonly pendingCallMode = signal<'audio' | 'video' | null>(null);

  readonly canUpload = computed(() => {
    const u = this.auth.currentUser();
    if (!u) return false;
    const o = this.oturum();
    if (!o) return false;
    if (!(u.rol === 'Sistem' || u.rol === 'SayimBaskani')) return false;
    return o.durum === 'excel_bekleniyor' || o.durum === 'taslak' || o.durum === 'aktif';
  });

  readonly canLockUnlock = computed(() => {
    const r = this.auth.currentUser()?.rol;
    return r === 'Sistem' || r === 'SayimBaskani';
  });

  readonly durumLabel = (d: OturumDurum) => OTURUM_DURUM_LABELS[d];
  readonly durumColor = (d: OturumDurum) => OTURUM_DURUM_COLOR[d];
  readonly urunDurumLabel = (d: UrunDurum) => URUN_DURUM_LABELS[d];

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) this.load(id);
    // pendingCall yakalama constructor'daki effect'te yapılıyor.
  }

  openLive(): void {
    this.pendingCallMode.set(null);
    this.liveOpen.set(true);
  }

  closeLive(): void {
    this.liveOpen.set(false);
    this.pendingCallMode.set(null);
    const o = this.oturum();
    if (o) this.load(o.id);
  }

  private load(id: string): void {
    this.loading.set(true);
    this.svc.get(id).subscribe({
      next: (o) => { this.oturum.set(o); this.loading.set(false); },
      error: () => { this.loading.set(false); this.toast.error('Oturum bulunamadı.'); },
    });
  }

  onImport(payload: { mapping: any; urunler: any[] }): void {
    const o = this.oturum();
    if (!o) return;
    this.uploading.set(true);
    this.svc.importExcel(o.id, payload as any).subscribe({
      next: (updated) => {
        this.uploading.set(false);
        this.oturum.set(updated);
        this.toast.success(`${updated.urunler.length} ürün yüklendi.`);
      },
      error: (err: HttpErrorResponse) => {
        this.uploading.set(false);
        this.toast.error(err.error?.message ?? 'Excel yükleme başarısız.');
      },
    });
  }

  async lock(): Promise<void> {
    const o = this.oturum();
    if (!o) return;
    const ok = await this.confirm.ask({
      title: 'Oturumu kilitle',
      message: 'Kilitlenince satır düzenlemesi durur. Açılabilir.',
      confirmLabel: 'Kilitle',
    });
    if (!ok) return;
    this.svc.changeDurum(o.id, 'kilitli').subscribe({
      next: (u) => { this.oturum.set(u); this.toast.success('Oturum kilitlendi.'); },
      error: (err: HttpErrorResponse) => this.toast.error(err.error?.message ?? 'Kilitlenemedi.'),
    });
  }

  unlock(): void {
    const o = this.oturum();
    if (!o) return;
    this.svc.changeDurum(o.id, 'aktif').subscribe({
      next: (u) => { this.oturum.set(u); this.toast.success('Oturum yeniden aktif.'); },
      error: (err: HttpErrorResponse) => this.toast.error(err.error?.message ?? 'İşlem başarısız.'),
    });
  }

  async complete(): Promise<void> {
    const o = this.oturum();
    if (!o) return;
    const ok = await this.confirm.ask({
      title: 'Oturumu kapat',
      message: 'Tamamlandı olarak işaretlensin mi? Bu işlem oturumu salt-okunur yapar.',
      confirmLabel: 'Kapat',
    });
    if (!ok) return;
    this.svc.changeDurum(o.id, 'tamamlandi').subscribe({
      next: (u) => { this.oturum.set(u); this.toast.success('Oturum kapatıldı.'); },
      error: (err: HttpErrorResponse) => this.toast.error(err.error?.message ?? 'Kapatma başarısız.'),
    });
  }
}
