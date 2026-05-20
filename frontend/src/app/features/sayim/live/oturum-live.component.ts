import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnDestroy,
  OnInit,
  ViewChild,
  ViewEncapsulation,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { AgGridAngular } from 'ag-grid-angular';
import {
  CellEditingStartedEvent,
  CellEditingStoppedEvent,
  ColDef,
  GridApi,
  GridReadyEvent,
  RowClassRules,
} from 'ag-grid-community';
import { Subject, takeUntil } from 'rxjs';
import * as XLSX from 'xlsx';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { ModalComponent } from '../../../shared/ui/modal/modal.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ConfirmService } from '../../../shared/ui/confirm/confirm.service';
import { AuthService } from '../../../core/auth/auth.service';
import { OturumService } from '../oturum.service';
import { ExcelUploadComponent } from '../oturum-detail/excel-upload.component';
import { CallPanelComponent } from './call-panel.component';
import { CallService } from './call.service';
import { ExcelImportPayload } from '../sayim.models';
import {
  FIRMA_TIP_PROFILI,
  OTURUM_DURUM_COLOR,
  OTURUM_DURUM_LABELS,
  OturumDetail,
  OturumDurum,
  OturumUrun,
  URUN_DURUM_LABELS,
  UrunDegisiklikTalebi,
  UrunDurum,
} from '../sayim.models';
import { SayimHubService } from './sayim-hub.service';

interface CellLockState {
  byUserId: string;
  byUserAdi: string;
  expiresAt: number;
}

interface PresenceUser {
  kullaniciId: string;
  kullaniciAdi: string;
  rol: string;
}

interface ActivityEntry {
  id: number;
  kind: 'join' | 'leave' | 'update' | 'comment' | 'talep' | 'talep-onay' | 'talep-red';
  time: Date;
  text: string;
  /** Bekleyen talep entry'leri için, başkana karar butonları gösterilir. */
  talep?: UrunDegisiklikTalebi;
  /** Karara bağlanan talep id'si — bu id ile beklemede kartı kaldırılır. */
  talepId?: string;
}

@Component({
  selector: 'app-oturum-live',
  standalone: true,
  imports: [PageHeaderComponent, ModalComponent, AgGridAngular, RouterLink, FormsModule, ExcelUploadComponent, CallPanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './oturum-live.component.html',
  styleUrl: './oturum-live.component.css',
  encapsulation: ViewEncapsulation.None,
})
export class OturumLiveComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly svc = inject(OturumService);
  private readonly hub = inject(SayimHubService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly auth = inject(AuthService);

  /** Modal/popup içinden çağrıldığında oturum id'sini route yerine input'tan al. */
  @Input() oturumIdInput?: string;
  /** Embedded mod: page-header gizlenir, izgara modal'ın boyuna esner. */
  @Input() embedded = false;
  /** Embedded modda otomatik görüntülü/sesli arama başlat (davet kabulünden sonra). */
  @Input() autoCallMode: 'audio' | 'video' | null = null;

  @ViewChild(AgGridAngular) private gridRef?: AgGridAngular;
  private gridApi?: GridApi<OturumUrun>;
  private readonly destroy$ = new Subject<void>();
  private nextActivityId = 0;

  readonly oturum = signal<OturumDetail | null>(null);
  readonly loading = signal(true);
  readonly presence = signal<PresenceUser[]>([]);
  readonly activity = signal<ActivityEntry[]>([]);

  // Cell lock map: `${urunId}|${alan}` → state
  private readonly lockMap = new Map<string, CellLockState>();
  readonly lockTick = signal(0); // bumped to trigger refresh of cell classes

  // Comments side panel
  readonly selectedUrunId = signal<string | null>(null);
  readonly newComment = signal('');

  // Filter tabs
  readonly filterTab = signal<'all' | 'beklemede' | 'tekrar' | 'onay'>('all');
  readonly searchQuery = signal('');

  // Activity panel — talep filtreleri (embedded modda kategori/alt kategori bazlı)
  readonly talepKategoriFilter = signal<string>('');
  readonly talepAltKategoriFilter = signal<string>('');

  // Arama servisi — call-panel komponenti üzerinden kullanılıyor.
  protected readonly call = inject(CallService);

  // Row edit modal (admin)
  readonly editingRow = signal<OturumUrun | null>(null);
  readonly editForm = signal<{ barkod: string; urunAdi: string; sistemStok: number; sayilanStok: number; durum: UrunDurum }>({
    barkod: '', urunAdi: '', sistemStok: 0, sayilanStok: 0, durum: 'beklemede',
  });
  readonly editSaving = signal(false);

  // Excel re-upload modal
  readonly excelReuploadOpen = signal(false);
  readonly excelUploading = signal(false);

  readonly currentUserId = computed(() => this.auth.currentUser()?.id ?? null);
  readonly isAdmin = computed(() => {
    const r = this.auth.currentUser()?.rol;
    return r === 'Sistem' || r === 'SayimBaskani';
  });
  readonly canEditDurum = computed(() => this.isAdmin());

  readonly durumLabel = (d: OturumDurum) => OTURUM_DURUM_LABELS[d];
  readonly durumColor = (d: OturumDurum) => OTURUM_DURUM_COLOR[d];
  readonly urunDurumLabel = (d: UrunDurum) => URUN_DURUM_LABELS[d];

  readonly urunDurumOptions: UrunDurum[] = [
    'beklemede', 'tekrar_sayiliyor', 'onaylandi', 'iptal', 'incele',
  ];

  private readonly tlFormatter = (v: unknown): string => {
    if (v === null || v === undefined || v === '' || !Number.isFinite(Number(v))) return '—';
    return Number(v).toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' ₺';
  };

  readonly columnDefs = computed<ColDef<OturumUrun>[]>(() => {
    const o = this.oturum();
    const tip = o?.firmaTip ?? 'diger';
    const profil = FIRMA_TIP_PROFILI[tip] ?? [];

    const profilCols: ColDef<OturumUrun>[] = profil.map((k) => ({
      field: k.key as keyof OturumUrun,
      headerName: k.label,
      width: 130,
      valueFormatter: (p) => (p.value ?? '—') as string,
    }));

    return [
      {
        field: 'barkod',
        headerName: 'Barkod',
        minWidth: 140,
        cellClass: 'font-mono text-xs',
      },
      { field: 'urunAdi', headerName: 'Ürün adı', flex: 1, minWidth: 180 },
      ...profilCols,
      {
        field: 'sistemStok',
        headerName: 'Sistem',
        width: 100,
        cellClass: 'font-mono',
      },
      {
        field: 'sayilanStok',
        headerName: '✎ Fiili',
        headerTooltip: 'Çift tıkla — düzenle',
        width: 110,
        cellClass: (p) => {
          const lock = this.cellClassFor(p.data, 'sayilanStok');
          const pending = this.hasPendingTalep(p.data, 'sayilanStok') ? ' cell-pending' : '';
          return lock + pending + ' editable-cell';
        },
        tooltipValueGetter: (p) => {
          const lockTip = this.cellLockTooltip(p.data, 'sayilanStok');
          if (lockTip) return lockTip;
          if (this.hasPendingTalep(p.data, 'sayilanStok')) {
            const t = (p.data?.acikTalepler ?? []).find(
              (x) => x.alan === 'sayilanStok' && x.durum === 'beklemede');
            return t ? `Bekleyen talep: ${t.kullaniciAdi} (${t.eskiDeger} → ${t.yeniDeger})` : 'Bekleyen değişiklik talebi var';
          }
          return 'Çift tıkla — düzenle';
        },
        editable: (p) => this.canEditSayilan(p.data) && !this.isCellLockedByOther(p.data, 'sayilanStok'),
        valueParser: (p) => Number(p.newValue),
      },
      {
        field: 'fark',
        headerName: 'Adet Farkı',
        width: 110,
        cellClass: (p) =>
          ((p.value ?? 0) > 0 ? 'text-accent-success ' : (p.value ?? 0) < 0 ? 'text-accent-danger ' : 'text-text-muted ') + 'font-mono',
        valueFormatter: (p) => ((p.value ?? 0) > 0 ? '+' : '') + (p.value ?? 0),
      },
      {
        field: 'fiyat',
        headerName: 'Fiyat',
        width: 110,
        cellClass: 'font-mono text-text-secondary',
        valueFormatter: (p) => this.tlFormatter(p.value),
      },
      {
        field: 'sistemFarki',
        headerName: 'Sistem Fiyatı',
        width: 130,
        cellClass: 'font-mono',
        valueFormatter: (p) => this.tlFormatter(p.value),
      },
      {
        field: 'fiiliFarki',
        headerName: 'Fiili Fiyatı',
        width: 130,
        cellClass: 'font-mono',
        valueFormatter: (p) => this.tlFormatter(p.value),
      },
      {
        field: 'fiyatFarki',
        headerName: 'Fiyat Farkı',
        width: 130,
        cellClass: (p) =>
          ((p.value ?? 0) > 0 ? 'text-accent-success ' : (p.value ?? 0) < 0 ? 'text-accent-danger ' : 'text-text-muted ') + 'font-mono',
        valueFormatter: (p) => this.tlFormatter(p.value),
      },
      {
        field: 'durum',
        headerName: this.isAdmin() ? '▾ Durum' : 'Durum',
        headerTooltip: this.isAdmin() ? 'Çift tıkla — listeden seç' : 'Durum',
        width: 150,
        editable: () => this.isAdmin(),
        cellEditor: 'agSelectCellEditor',
        cellEditorParams: { values: this.urunDurumOptions },
        valueFormatter: (p) => this.urunDurumLabel(p.value as UrunDurum),
        cellClass: (p) => this.cellClassFor(p.data, 'durum') + (this.isAdmin() ? ' editable-cell' : ''),
        tooltipValueGetter: () => this.isAdmin() ? 'Çift tıkla — listeden seç' : '',
      },
      {
        field: 'atananSaymanAdi',
        headerName: 'Atanan',
        width: 140,
        editable: false,
        valueFormatter: (p) => p.value ?? '—',
      },
      {
        field: 'yorumSayisi',
        headerName: 'Yorum',
        width: 90,
        cellClass: 'font-mono cursor-pointer',
        onCellClicked: (e) => { if (e.data) this.openComments(e.data.id); },
      },
      {
        headerName: 'İşlem',
        width: 130,
        sortable: false,
        filter: false,
        hide: !this.isAdmin(),
        cellRenderer: (p: { data?: OturumUrun }) => {
          if (!p.data) return '';
          return `
            <span class="row-actions">
              <button data-action="edit" data-id="${p.data.id}" class="row-action-btn" title="Satırı düzenle">Düzenle</button>
              <button data-action="delete" data-id="${p.data.id}" class="row-action-btn row-action-danger" title="Satırı sil">Sil</button>
            </span>
          `;
        },
        onCellClicked: (e) => {
          const target = e.event?.target as HTMLElement | undefined;
          if (!target) return;
          const btn = target.closest('button[data-action]') as HTMLButtonElement | null;
          if (!btn || !e.data) return;
          const action = btn.dataset['action'];
          if (action === 'edit') this.openRowEdit(e.data);
          else if (action === 'delete') this.deleteRow(e.data);
        },
      },
    ];
  });

  readonly rowClassRules: RowClassRules<OturumUrun> = {
    'row-tekrar-say': (p) => p.data?.durum === 'tekrar_sayiliyor',
    'row-onaylandi': (p) => p.data?.durum === 'onaylandi',
    'row-iptal': (p) => p.data?.durum === 'iptal',
  };

  readonly filteredUrunler = computed(() => {
    const o = this.oturum();
    if (!o) return [];
    const tab = this.filterTab();
    const q = this.searchQuery().toLowerCase().trim();
    return o.urunler.filter((u) => {
      if (tab === 'beklemede' && u.durum !== 'beklemede') return false;
      if (tab === 'tekrar' && u.durum !== 'tekrar_sayiliyor') return false;
      if (tab === 'onay' && u.durum !== 'onaylandi') return false;
      if (q && !u.barkod.toLowerCase().includes(q) && !u.urunAdi.toLowerCase().includes(q)) return false;
      return true;
    });
  });

  readonly selectedUrun = computed(() => {
    const o = this.oturum();
    const id = this.selectedUrunId();
    if (!o || !id) return null;
    return o.urunler.find((u) => u.id === id) ?? null;
  });

  readonly getRowId = (p: { data: OturumUrun }) => p.data.id;

  ngOnInit(): void {
    const id = this.oturumIdInput ?? this.route.snapshot.paramMap.get('id');
    if (!id) return;
    this.load(id);
    this.subscribeHub();
    this.hub.joinOturum(id).catch(() => {
      this.toast.error('Canlı bağlantı kurulamadı.');
    });
    // Embedded modda parent input'u öncelikli; full page modda query param'a bak.
    let callMode: 'audio' | 'video' | null = this.autoCallMode ?? null;
    if (!this.embedded && !callMode) {
      const q = this.route.snapshot.queryParamMap.get('call');
      if (q === 'audio' || q === 'video') callMode = q;
    }
    const sessionKey = `autocall:${id}`;
    if (callMode && !sessionStorage.getItem(sessionKey)) {
      sessionStorage.setItem(sessionKey, '1');
      if (!this.embedded) {
        this.router.navigate([], {
          relativeTo: this.route,
          queryParams: { call: null },
          queryParamsHandling: 'merge',
          replaceUrl: true,
        });
      }
      setTimeout(() => {
        this.call.start(id, callMode!).catch(() => undefined);
      }, 800);
    }
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    // Sayfadan ayrılınca autocall flag'ini temizle — yeni davet geldiğinde tekrar autostart edilebilsin.
    const id = this.oturumIdInput ?? this.route.snapshot.paramMap.get('id');
    if (id) sessionStorage.removeItem(`autocall:${id}`);
    // Hub bağlantısını koparmıyoruz — global ringing dinleyicisi diğer sayfalarda da çalışsın.
    this.hub.leaveOturum().catch(() => undefined);
    this.call.stop().catch(() => undefined);
  }

  onGridReady(e: GridReadyEvent): void {
    this.gridApi = e.api;
  }

  onCellEditingStarted(e: CellEditingStartedEvent<OturumUrun>): void {
    const o = this.oturum();
    if (!o || !e.data) return;
    const alan = e.column.getColId();
    if (alan === 'sayilanStok' || alan === 'durum')
      this.hub.lockCell(o.id, e.data.id, alan).catch(() => undefined);
  }

  onCellEditingStopped(e: CellEditingStoppedEvent<OturumUrun>): void {
    const o = this.oturum();
    if (!o || !e.data) return;
    const alan = e.column.getColId();
    const oldValue = e.oldValue;
    const newValue = e.newValue;
    if (oldValue === newValue) {
      this.hub.releaseCell(o.id, e.data.id, alan).catch(() => undefined);
      return;
    }
    const revert = () => {
      const cur = this.oturum();
      if (!cur) return;
      const u = cur.urunler.find((x) => x.id === e.data!.id);
      if (!u) return;
      if (alan === 'sayilanStok') u.sayilanStok = oldValue ?? 0;
      else if (alan === 'durum') u.durum = oldValue;
      this.oturum.set({ ...cur });
      // Tüm satırı zorla yeniden çiz — talep akışında hesaplanan sütunlar
      // (Adet Farkı / Fiyatlar) editor sonrası boş görünmesin.
      this.gridApi?.refreshCells({ force: true });
    };

    if (alan === 'sayilanStok') {
      const num = Number(newValue);
      if (!Number.isFinite(num)) { revert(); return; }

      // Kullanici rolü → talep gönder; başkan/sistem → doğrudan yaz.
      const isKullanici = !this.isAdmin();
      if (isKullanici) {
        // Talep akışı: gridi gerçek değerine döndür — anlık değişimi onay event'i taşıyacak.
        revert();
        this.hub.releaseCell(o.id, e.data.id, 'sayilanStok').catch(() => undefined);
        this.hub.talepOlustur(o.id, e.data.id, 'sayilanStok', num).then(() => {
          this.toast.success('Talep gönderildi — sayım başkanı onayını bekliyor.');
        }).catch((err) => {
          const msg = (err as { message?: string })?.message ?? '';
          this.toast.error(msg.includes('zaten bekleyen') ? 'Bu hücre için zaten bekleyen talebin var.' : 'Talep gönderilemedi.');
        });
        return;
      }

      this.hub.updateUrun(o.id, e.data.id, { sayilanStok: num }).catch(() => {
        this.toast.error('Güncelleme reddedildi.');
        revert();
      });
    } else if (alan === 'durum') {
      this.hub.updateUrun(o.id, e.data.id, { durum: String(newValue) }).catch(() => {
        this.toast.error('Durum güncellenemedi.');
        revert();
      });
    }
  }

  private load(id: string): void {
    this.loading.set(true);
    this.svc.get(id).subscribe({
      next: (o) => {
        this.oturum.set(o);
        this.loading.set(false);
        // Sayfa yüklendiğinde halihazırdaki açık talepleri aktivite paneline bas.
        this.activity.set([]);
        for (const u of o.urunler) {
          for (const t of u.acikTalepler ?? []) {
            const alanLabel = t.alan === 'sayilanStok' ? 'Fiili' : t.alan;
            this.pushActivity(
              'talep',
              `${t.kullaniciAdi}: ${u.barkod} · ${alanLabel} ${t.eskiDeger ?? ''} → ${t.yeniDeger ?? ''}`,
              t,
            );
          }
        }
      },
      error: () => { this.loading.set(false); this.toast.error('Oturum yüklenemedi.'); },
    });
  }

  private subscribeHub(): void {
    this.hub.katildi$.pipe(takeUntil(this.destroy$)).subscribe((e) => {
      this.presence.update((list) =>
        list.some((p) => p.kullaniciId === e.kullaniciId)
          ? list
          : [...list, { kullaniciId: e.kullaniciId, kullaniciAdi: e.kullaniciAdi, rol: e.rol }]);
      this.pushActivity('join', `${e.kullaniciAdi} oturuma katıldı.`);
    });

    this.hub.ayrildi$.pipe(takeUntil(this.destroy$)).subscribe((e) => {
      const name = this.presence().find((p) => p.kullaniciId === e.kullaniciId)?.kullaniciAdi ?? '?';
      this.presence.update((list) => list.filter((p) => p.kullaniciId !== e.kullaniciId));
      this.pushActivity('leave', `${name} ayrıldı.`);
    });

    this.hub.hucreKilitlendi$.pipe(takeUntil(this.destroy$)).subscribe((e) => {
      const me = this.currentUserId();
      if (e.kullaniciId === me) return;
      const key = `${e.urunId}|${e.alan}`;
      this.lockMap.set(key, {
        byUserId: e.kullaniciId,
        byUserAdi: e.kullaniciAdi,
        expiresAt: new Date(e.expiresAt).getTime(),
      });
      this.lockTick.update((v) => v + 1);
      this.gridApi?.refreshCells({ force: true });
    });

    this.hub.hucreSerbest$.pipe(takeUntil(this.destroy$)).subscribe((e) => {
      this.lockMap.delete(`${e.urunId}|${e.alan}`);
      this.lockTick.update((v) => v + 1);
      this.gridApi?.refreshCells({ force: true });
    });

    this.hub.urunGuncellendi$.pipe(takeUntil(this.destroy$)).subscribe((e) => {
      const cur = this.oturum();
      if (!cur || cur.id !== e.oturumId) return;
      const u = cur.urunler.find((x) => x.id === e.patch.urunId);
      if (!u) return;
      if (e.patch.sayilanStok !== null && e.patch.sayilanStok !== undefined) u.sayilanStok = e.patch.sayilanStok;
      if (e.patch.durum) u.durum = e.patch.durum as UrunDurum;
      if (e.patch.atananSaymanId !== null && e.patch.atananSaymanId !== undefined)
        u.atananSaymanId = e.patch.atananSaymanId;
      u.fark = e.patch.fark;
      u.yorumSayisi = e.patch.yorumSayisi;
      if (typeof u.fiyat === 'number' && u.fiyat !== null) {
        u.sistemFarki = u.sistemStok * u.fiyat;
        u.fiiliFarki = u.sayilanStok * u.fiyat;
        u.fiyatFarki = (u.sayilanStok - u.sistemStok) * u.fiyat;
      }
      cur.ozetler.toplamUrun = e.ozet.toplamUrun;
      cur.ozetler.beklemedeSayisi = e.ozet.beklemede;
      cur.ozetler.tekrarSayilan = e.ozet.tekrarSayilan;
      cur.ozetler.onaylanmis = e.ozet.onaylanmis;
      cur.ozetler.iptalEdilmis = e.ozet.iptalEdilmis;
      cur.ozetler.inceleme = e.ozet.inceleme;
      cur.ozetler.toplamFarkPozitif = e.ozet.toplamFarkPozitif;
      cur.ozetler.toplamFarkNegatif = e.ozet.toplamFarkNegatif;
      this.oturum.set({ ...cur });
      this.pushActivity('update',
        `${e.patch.kullaniciAdi}: ${u.barkod}${e.patch.durum ? ' → ' + this.urunDurumLabel(e.patch.durum as UrunDurum) : ''}`);
    });

    this.hub.yorumEklendi$.pipe(takeUntil(this.destroy$)).subscribe((e) => {
      const cur = this.oturum();
      if (!cur || cur.id !== e.oturumId) return;
      const u = cur.urunler.find((x) => x.id === e.urunId);
      if (u) u.yorumSayisi += 1;
      this.pushActivity('comment', `${e.kullaniciAdi}: "${e.mesaj}"`);
      this.oturum.set({ ...cur });
    });

    this.hub.talepOlusturuldu$.pipe(takeUntil(this.destroy$)).subscribe((e) => {
      const cur = this.oturum();
      if (!cur || cur.id !== e.oturumId) return;
      const u = cur.urunler.find((x) => x.id === e.talep.urunId);
      if (!u) return;
      u.acikTalepler = [...(u.acikTalepler ?? []), e.talep];
      this.oturum.set({ ...cur });
      this.gridApi?.refreshCells({ force: true });
      const alanLabel = e.talep.alan === 'sayilanStok' ? 'Fiili' : e.talep.alan;
      this.pushActivity('talep',
        `${e.talep.kullaniciAdi}: ${u.barkod} · ${alanLabel} ${e.talep.eskiDeger ?? ''} → ${e.talep.yeniDeger ?? ''}`,
        e.talep);
    });

    this.hub.talepOnaylandi$.pipe(takeUntil(this.destroy$)).subscribe((e) => {
      const cur = this.oturum();
      if (!cur || cur.id !== e.oturumId) return;
      const u = cur.urunler.find((x) => x.id === e.urunId);
      if (!u) return;
      const acan = (u.acikTalepler ?? []).find((t) => t.id === e.talepId);
      u.acikTalepler = (u.acikTalepler ?? []).filter((t) => t.id !== e.talepId);
      this.oturum.set({ ...cur });
      this.gridApi?.refreshCells({ force: true });
      this.removePendingActivity(e.talepId);
      this.pushActivity('talep-onay', `${e.kararVerenAdi} talebi onayladı (${u.barkod}).`);
      if (acan && acan.kullaniciId === this.currentUserId()) {
        this.toast.success(`Talebin onaylandı: ${u.barkod}.`);
      }
    });

    this.hub.talepReddedildi$.pipe(takeUntil(this.destroy$)).subscribe((e) => {
      const cur = this.oturum();
      if (!cur || cur.id !== e.oturumId) return;
      const u = cur.urunler.find((x) => x.id === e.urunId);
      if (!u) return;
      const acan = (u.acikTalepler ?? []).find((t) => t.id === e.talepId);
      u.acikTalepler = (u.acikTalepler ?? []).filter((t) => t.id !== e.talepId);
      this.oturum.set({ ...cur });
      this.gridApi?.refreshCells({ force: true });
      this.removePendingActivity(e.talepId);
      const sebep = e.sebep ? ` — "${e.sebep}"` : '';
      this.pushActivity('talep-red', `${e.kararVerenAdi} talebi reddetti (${u.barkod})${sebep}.`);
      if (acan && acan.kullaniciId === this.currentUserId()) {
        this.toast.error(`Talebin reddedildi: ${u.barkod}${sebep}.`);
      }
    });

  }

  private cellClassFor(_data: OturumUrun | undefined, alan: string): string {
    void this.lockTick();
    if (!_data) return '';
    const key = `${_data.id}|${alan}`;
    const lock = this.lockMap.get(key);
    if (!lock) return '';
    if (lock.expiresAt < Date.now()) {
      this.lockMap.delete(key);
      return '';
    }
    return 'cell-locked';
  }

  private cellLockTooltip(data: OturumUrun | undefined, alan: string): string {
    if (!data) return '';
    const lock = this.lockMap.get(`${data.id}|${alan}`);
    return lock ? `${lock.byUserAdi} düzenliyor` : '';
  }

  private isCellLockedByOther(data: OturumUrun | undefined, alan: string): boolean {
    if (!data) return false;
    const lock = this.lockMap.get(`${data.id}|${alan}`);
    if (!lock || lock.expiresAt < Date.now()) return false;
    return lock.byUserId !== this.currentUserId();
  }

  private canEditSayilan(data: OturumUrun | undefined): boolean {
    if (!data) return false;
    const u = this.auth.currentUser();
    if (!u) return false;
    const o = this.oturum();
    if (!o || o.durum === 'kilitli' || o.durum === 'tamamlandi' || o.durum === 'iptal') return false;
    // Kullanici → talep akışı; her satır editable (kendi açtığı talep yoksa).
    if (u.rol === 'Kullanici') {
      return !(data.acikTalepler ?? []).some(
        (t) => t.alan === 'sayilanStok' && t.durum === 'beklemede' && t.kullaniciId === u.id,
      );
    }
    return true;
  }

  /** Bu hücrede herhangi bir bekleyen talep var mı? — sarı badge için. */
  private hasPendingTalep(data: OturumUrun | undefined, alan: string): boolean {
    if (!data) return false;
    return (data.acikTalepler ?? []).some(
      (t) => t.alan === alan && t.durum === 'beklemede',
    );
  }

  private pushActivity(
    kind: ActivityEntry['kind'],
    text: string,
    talep?: UrunDegisiklikTalebi,
  ): void {
    this.activity.update((list) => {
      const next = [
        ...list,
        {
          id: ++this.nextActivityId,
          kind,
          time: new Date(),
          text,
          talep,
          talepId: talep?.id,
        } as ActivityEntry,
      ];
      return next.slice(-100); // cap memory
    });
  }

  /** Karara bağlanan talebin "beklemede" kartını listeden çıkarır. */
  private removePendingActivity(talepId: string): void {
    this.activity.update((list) =>
      list.filter((a) => !(a.kind === 'talep' && a.talepId === talepId)),
    );
  }

  // ─── Talep onay/red (sayım başkanı) ──────────────────────────────────────
  onayaGonder(entry: ActivityEntry): void {
    if (!this.isAdmin() || !entry.talep) return;
    const o = this.oturum();
    if (!o) return;
    this.hub.talepOnayla(o.id, entry.talep.urunId, entry.talep.id).catch(() => {
      this.toast.error('Talep onaylanamadı.');
    });
  }

  async redde(entry: ActivityEntry): Promise<void> {
    if (!this.isAdmin() || !entry.talep) return;
    const o = this.oturum();
    if (!o) return;
    const ok = await this.confirm.ask({
      title: 'Talebi reddet',
      message: `${entry.talep.kullaniciAdi} tarafından açılan talep reddedilecek. Devam edilsin mi?`,
      confirmLabel: 'Reddet',
      cancelLabel: 'Vazgeç',
      danger: true,
    });
    if (!ok) return;
    this.hub.talepReddet(o.id, entry.talep.urunId, entry.talep.id).catch(() => {
      this.toast.error('Talep reddedilemedi.');
    });
  }

  setFilter(t: 'all' | 'beklemede' | 'tekrar' | 'onay'): void {
    this.filterTab.set(t);
  }

  openComments(urunId: string): void {
    this.selectedUrunId.set(urunId);
    this.newComment.set('');
  }

  closeComments(): void {
    this.selectedUrunId.set(null);
  }

  submitComment(): void {
    const o = this.oturum();
    const u = this.selectedUrun();
    const msg = this.newComment().trim();
    if (!o || !u || !msg) return;
    this.hub.updateUrun(o.id, u.id, { yorum: msg }).catch(() => {
      this.toast.error('Yorum gönderilemedi.');
    });
    this.newComment.set('');
  }

  formatTime(d: Date): string {
    return d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  }

  getTalepUrunBarkod(urunId: string): string {
    return this.oturum()?.urunler.find((u) => u.id === urunId)?.barkod ?? '';
  }

  private getTalepUrun(urunId: string): OturumUrun | undefined {
    return this.oturum()?.urunler.find((u) => u.id === urunId);
  }

  /** Talep aktivite entry'lerinden, ürünün kategori bilgisine göre süzülmüş liste — yeniden eskiye sıralı. */
  readonly filteredTalepActivities = computed(() => {
    const k = this.talepKategoriFilter().trim();
    const ak = this.talepAltKategoriFilter().trim();
    return this.activity()
      .filter((a) => a.kind === 'talep')
      .filter((a) => {
        if (!a.talep) return false;
        if (!k && !ak) return true;
        const u = this.getTalepUrun(a.talep.urunId);
        if (!u) return false;
        if (k && (u.kategori ?? '') !== k) return false;
        if (ak && (u.altKategori ?? '') !== ak) return false;
        return true;
      })
      .slice()
      .reverse();
  });

  /** Talep dışı (join/leave/update/comment/talep-onay/talep-red) aktiviteler — yeniden eskiye sıralı. */
  readonly otherActivities = computed(() =>
    this.activity().filter((a) => a.kind !== 'talep').slice().reverse(),
  );

  /** Aktif (bekleyen) talepleri olan ürünlerin kategori listesi — filtre dropdown'u için. */
  readonly availableTalepKategoriler = computed(() => {
    const set = new Set<string>();
    for (const a of this.activity()) {
      if (a.kind !== 'talep' || !a.talep) continue;
      const u = this.getTalepUrun(a.talep.urunId);
      const k = u?.kategori?.trim();
      if (k) set.add(k);
    }
    return [...set].sort((x, y) => x.localeCompare(y, 'tr'));
  });

  /** Seçili kategoriye göre alt kategoriler — kategori boşsa hepsini döner. */
  readonly availableTalepAltKategoriler = computed(() => {
    const k = this.talepKategoriFilter().trim();
    const set = new Set<string>();
    for (const a of this.activity()) {
      if (a.kind !== 'talep' || !a.talep) continue;
      const u = this.getTalepUrun(a.talep.urunId);
      if (!u) continue;
      if (k && (u.kategori ?? '') !== k) continue;
      const ak = u.altKategori?.trim();
      if (ak) set.add(ak);
    }
    return [...set].sort((x, y) => x.localeCompare(y, 'tr'));
  });

  setTalepKategori(v: string): void {
    this.talepKategoriFilter.set(v);
    // Kategori değişti — alt kategori listesi değişebilir, mevcut seçim hâlâ geçerli mi kontrol et.
    const ak = this.talepAltKategoriFilter();
    if (ak && !this.availableTalepAltKategoriler().includes(ak)) {
      this.talepAltKategoriFilter.set('');
    }
  }

  setTalepAltKategori(v: string): void {
    this.talepAltKategoriFilter.set(v);
  }

  updateEditForm<K extends 'barkod' | 'urunAdi' | 'sistemStok' | 'sayilanStok' | 'durum'>(
    key: K,
    value: string,
  ): void {
    const cur = this.editForm();
    if (key === 'sistemStok' || key === 'sayilanStok') {
      this.editForm.set({ ...cur, [key]: Number(value) });
    } else if (key === 'durum') {
      this.editForm.set({ ...cur, durum: value as UrunDurum });
    } else {
      this.editForm.set({ ...cur, [key]: value });
    }
  }

  // ─── Row edit (admin) ────────────────────────────────────────────────────
  openRowEdit(u: OturumUrun): void {
    if (!this.isAdmin()) return;
    this.editingRow.set(u);
    this.editForm.set({
      barkod: u.barkod,
      urunAdi: u.urunAdi,
      sistemStok: u.sistemStok,
      sayilanStok: u.sayilanStok,
      durum: u.durum,
    });
  }

  closeRowEdit(): void {
    this.editingRow.set(null);
    this.editSaving.set(false);
  }

  submitRowEdit(): void {
    const o = this.oturum();
    const u = this.editingRow();
    const f = this.editForm();
    if (!o || !u) return;

    const body: Partial<{
      sayilanStok: number; durum: string; barkod: string; urunAdi: string; sistemStok: number;
    }> = {};
    if (f.barkod.trim() && f.barkod.trim() !== u.barkod) body.barkod = f.barkod.trim();
    if (f.urunAdi.trim() !== u.urunAdi) body.urunAdi = f.urunAdi.trim();
    if (f.sistemStok !== u.sistemStok) body.sistemStok = Number(f.sistemStok);
    if (f.sayilanStok !== u.sayilanStok) body.sayilanStok = Number(f.sayilanStok);
    if (f.durum !== u.durum) body.durum = f.durum;

    if (Object.keys(body).length === 0) {
      this.closeRowEdit();
      return;
    }

    this.editSaving.set(true);
    this.svc.patchUrun(o.id, u.id, body).subscribe({
      next: () => {
        this.toast.success('Satır güncellendi.');
        this.closeRowEdit();
        this.load(o.id); // refresh full oturum (recomputes ozet)
      },
      error: (err: HttpErrorResponse) => {
        this.editSaving.set(false);
        this.toast.error(err.error?.message ?? 'Satır güncellenemedi.');
      },
    });
  }

  // ─── Row delete (admin) ──────────────────────────────────────────────────
  async deleteRow(u: OturumUrun): Promise<void> {
    if (!this.isAdmin()) return;
    const o = this.oturum();
    if (!o) return;
    const ok = await this.confirm.ask({
      title: 'Satırı sil',
      message: `"${u.barkod} · ${u.urunAdi}" satırı kalıcı olarak silinecek. Devam edilsin mi?`,
      confirmLabel: 'Sil',
      cancelLabel: 'Vazgeç',
      danger: true,
    });
    if (!ok) return;

    this.svc.deleteUrun(o.id, u.id).subscribe({
      next: () => {
        this.toast.success('Satır silindi.');
        this.load(o.id);
      },
      error: (err: HttpErrorResponse) => {
        this.toast.error(err.error?.message ?? 'Satır silinemedi.');
      },
    });
  }

  // ─── Excel re-upload (admin / yönetici) ──────────────────────────────────
  openExcelReupload(): void {
    this.excelReuploadOpen.set(true);
  }

  closeExcelReupload(): void {
    if (this.excelUploading()) return;
    this.excelReuploadOpen.set(false);
  }

  onExcelReimport(payload: ExcelImportPayload): void {
    const o = this.oturum();
    if (!o) return;
    this.excelUploading.set(true);
    this.svc.importExcel(o.id, payload).subscribe({
      next: () => {
        this.toast.success('Excel yeniden yüklendi.');
        this.excelUploading.set(false);
        this.excelReuploadOpen.set(false);
        this.load(o.id);
      },
      error: (err: HttpErrorResponse) => {
        this.excelUploading.set(false);
        const errs = err.error?.errors as Record<string, string[]> | undefined;
        const first = errs ? Object.values(errs).flat()[0] : undefined;
        this.toast.error(first ?? err.error?.message ?? 'Excel yüklenemedi.');
      },
    });
  }

  // ─── Karşılaştırma raporunu dışa aktar ───────────────────────────────────
  exportExcel(): void {
    const o = this.oturum();
    if (!o) return;
    const profil = FIRMA_TIP_PROFILI[o.firmaTip] ?? [];
    const headers = [
      'Barkod', 'Ürün adı',
      ...profil.map((p) => p.label),
      'Sistem', 'Fiili', 'Adet Farkı',
      'Fiyat', 'Sistem Fiyatı', 'Fiili Fiyatı', 'Fiyat Farkı',
      'Durum',
    ];
    const rows = o.urunler.map((u) => [
      u.barkod,
      u.urunAdi,
      ...profil.map((p) => (u as unknown as Record<string, unknown>)[p.key] ?? ''),
      u.sistemStok,
      u.sayilanStok,
      u.fark,
      u.fiyat ?? '',
      u.sistemFarki ?? '',
      u.fiiliFarki ?? '',
      u.fiyatFarki ?? '',
      this.urunDurumLabel(u.durum),
    ]);
    const ws = XLSX.utils.aoa_to_sheet([headers, ...rows]);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Karşılaştırma');
    const fileName = this.buildFileName(o, 'xlsx');
    XLSX.writeFile(wb, fileName);
    this.toast.success('Excel indiriliyor.');
  }

  exportTxt(): void {
    const o = this.oturum();
    if (!o) return;
    // Format: barkod;fiili (her satırda bir ürün).
    const lines = o.urunler.map((u) => `${u.barkod};${u.sayilanStok}`);
    const blob = new Blob([lines.join('\r\n')], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = this.buildFileName(o, 'txt');
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
    this.toast.success('TXT indiriliyor.');
  }

  private buildFileName(o: OturumDetail, ext: string): string {
    const slug = (s: string) => s
      .toLocaleLowerCase('tr-TR')
      .replace(/[ğ]/g, 'g').replace(/[ü]/g, 'u').replace(/[ş]/g, 's')
      .replace(/[ı]/g, 'i').replace(/[ö]/g, 'o').replace(/[ç]/g, 'c')
      .replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '');
    const tarih = (o.tarih ?? '').slice(0, 10);
    return `${slug(o.firmaAdi)}_${slug(o.magazaAdi)}_${tarih}.${ext}`;
  }

  readonly canReuploadExcel = computed(() => {
    const u = this.auth.currentUser();
    const o = this.oturum();
    if (!u || !o) return false;
    if (u.rol !== 'Sistem' && u.rol !== 'SayimBaskani') return false;
    return o.durum !== 'kilitli' && o.durum !== 'tamamlandi' && o.durum !== 'iptal';
  });
}
