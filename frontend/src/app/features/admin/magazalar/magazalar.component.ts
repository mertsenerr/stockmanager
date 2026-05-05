import { HttpErrorResponse } from '@angular/common/http';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
  ViewEncapsulation,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import * as L from 'leaflet';
import { ModalComponent } from '../../../shared/ui/modal/modal.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { ConfirmService } from '../../../shared/ui/confirm/confirm.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { FirmaService } from '../firma.service';
import { MagazaService } from '../magaza.service';
import { KullaniciService } from '../kullanici.service';
import { Firma, KullaniciList, Magaza } from '../admin.models';

type ViewMode = 'list' | 'map';

@Component({
  selector: 'app-magazalar',
  standalone: true,
  imports: [ReactiveFormsModule, ModalComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './magazalar.component.html',
  styleUrl: './magazalar.component.css',
  encapsulation: ViewEncapsulation.None,
})
export class MagazalarComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly mSvc = inject(MagazaService);
  private readonly fSvc = inject(FirmaService);
  private readonly uSvc = inject(KullaniciService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  @ViewChild('mapEl') private mapEl?: ElementRef<HTMLDivElement>;
  private map?: L.Map;
  private markerLayer?: L.LayerGroup;

  readonly magazalar = signal<Magaza[]>([]);
  readonly firmalar = signal<Firma[]>([]);
  readonly mudurAdaylari = signal<KullaniciList[]>([]);
  readonly loading = signal(false);
  readonly query = signal('');
  readonly firmaFilter = signal<string>('');
  readonly includeInactive = signal(false);
  readonly view = signal<ViewMode>('list');

  readonly editing = signal<Magaza | null>(null);
  readonly modalOpen = signal(false);
  readonly saving = signal(false);
  readonly serverError = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    firmaId: ['', [Validators.required]],
    ad: ['', [Validators.required, Validators.maxLength(120)]],
    sehir: ['', [Validators.required]],
    ilce: ['', [Validators.required]],
    adres: ['', [Validators.required]],
    lat: [null as number | null],
    lng: [null as number | null],
    muduruKullaniciId: [''],
    aktifMi: [true],
  });

  readonly filtered = computed(() => {
    const q = this.query().toLowerCase().trim();
    const fid = this.firmaFilter();
    return this.magazalar().filter((m) => {
      const matchesFirma = !fid || m.firmaId === fid;
      const matchesQuery = !q
        || m.ad.toLowerCase().includes(q)
        || m.sehir.toLowerCase().includes(q)
        || m.ilce.toLowerCase().includes(q);
      return matchesFirma && matchesQuery;
    });
  });

  readonly withCoordCount = computed(
    () => this.filtered().filter((m) => m.koordinat).length,
  );

  constructor() {
    effect(() => {
      // Re-render markers when filtered list changes and we are in map mode.
      if (this.view() === 'map') this.renderMarkers(this.filtered());
    });
  }

  ngOnInit(): void {
    this.refresh();
    this.fSvc.list(false).subscribe({ next: (r) => this.firmalar.set(r) });
    this.uSvc.list(false).subscribe({
      next: (r) =>
        this.mudurAdaylari.set(
          r.filter((u) => u.rol === 'SayimBaskani' || u.rol === 'Kullanici'),
        ),
    });
  }

  ngAfterViewInit(): void {
    // Map is created lazily when user switches to map view.
  }

  ngOnDestroy(): void {
    this.map?.remove();
  }

  refresh(): void {
    this.loading.set(true);
    this.mSvc.list({
      firmaId: this.firmaFilter() || undefined,
      includeInactive: this.includeInactive(),
    }).subscribe({
      next: (r) => {
        this.magazalar.set(r);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.error('Mağazalar yüklenemedi.');
      },
    });
  }

  toggleInactive(): void {
    this.includeInactive.update((v) => !v);
    this.refresh();
  }

  setView(v: ViewMode): void {
    this.view.set(v);
    if (v === 'map') {
      // Defer to next tick so the map element is in the DOM.
      queueMicrotask(() => this.ensureMap());
    }
  }

  private ensureMap(): void {
    if (this.map || !this.mapEl) return;
    this.map = L.map(this.mapEl.nativeElement, {
      center: [39.0, 35.0], // approx Türkiye centroid
      zoom: 5,
      zoomControl: true,
    });
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '© OpenStreetMap',
      maxZoom: 19,
    }).addTo(this.map);
    this.markerLayer = L.layerGroup().addTo(this.map);
    this.renderMarkers(this.filtered());
  }

  private renderMarkers(list: Magaza[]): void {
    if (!this.map || !this.markerLayer) return;
    this.markerLayer.clearLayers();

    const withCoords = list.filter((m) => m.koordinat);
    const dot = L.divIcon({
      className: '',
      html: '<div style="width:10px;height:10px;border-radius:9999px;background:#3b82f6;border:2px solid #fafafa;box-shadow:0 0 0 1px #1f1f1f"></div>',
      iconSize: [14, 14],
      iconAnchor: [7, 7],
    });

    for (const m of withCoords) {
      L.marker([m.koordinat!.lat, m.koordinat!.lng], { icon: dot })
        .addTo(this.markerLayer!)
        .bindPopup(
          `<strong>${escapeHtml(m.ad)}</strong><br/>` +
          `<span style="color:#a1a1aa">${escapeHtml(m.firmaAdi ?? '')}</span><br/>` +
          `<span style="color:#71717a">${escapeHtml(m.sehir + ' / ' + m.ilce)}</span>`,
        );
    }

    if (withCoords.length > 0) {
      const bounds = L.latLngBounds(withCoords.map((m) => [m.koordinat!.lat, m.koordinat!.lng] as [number, number]));
      this.map.fitBounds(bounds, { padding: [40, 40], maxZoom: 13 });
    }
  }

  openCreate(): void {
    this.editing.set(null);
    this.serverError.set(null);
    this.form.reset({
      firmaId: '', ad: '', sehir: '', ilce: '', adres: '',
      lat: null, lng: null, muduruKullaniciId: '', aktifMi: true,
    });
    this.modalOpen.set(true);
  }

  openEdit(m: Magaza): void {
    this.editing.set(m);
    this.serverError.set(null);
    this.form.reset({
      firmaId: m.firmaId,
      ad: m.ad,
      sehir: m.sehir,
      ilce: m.ilce,
      adres: m.adres,
      lat: m.koordinat?.lat ?? null,
      lng: m.koordinat?.lng ?? null,
      muduruKullaniciId: m.muduruKullaniciId ?? '',
      aktifMi: m.aktifMi,
    });
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
  }

  submit(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const koordinat = v.lat !== null && v.lng !== null
      ? { lat: Number(v.lat), lng: Number(v.lng) }
      : null;
    const payload = {
      firmaId: v.firmaId,
      ad: v.ad.trim(),
      sehir: v.sehir.trim(),
      ilce: v.ilce.trim(),
      adres: v.adres.trim(),
      koordinat,
      muduruKullaniciId: v.muduruKullaniciId || null,
      aktifMi: v.aktifMi,
    };
    this.saving.set(true);
    this.serverError.set(null);

    const editing = this.editing();
    const op = editing
      ? this.mSvc.update(editing.id, payload)
      : this.mSvc.create(payload);

    op.subscribe({
      next: () => {
        this.saving.set(false);
        this.modalOpen.set(false);
        this.toast.success(editing ? 'Mağaza güncellendi.' : 'Mağaza oluşturuldu.');
        this.refresh();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.serverError.set(err.error?.message ?? 'İşlem başarısız.');
      },
    });
  }

  async remove(m: Magaza): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Mağazayı sil',
      message: `${m.ad} silinsin mi? Pasif duruma geçer, geri alınabilir.`,
      confirmLabel: 'Sil',
      danger: true,
    });
    if (!ok) return;
    this.mSvc.remove(m.id).subscribe({
      next: () => {
        this.toast.success('Mağaza silindi.');
        this.refresh();
      },
      error: (err: HttpErrorResponse) =>
        this.toast.error(err.error?.message ?? 'Silme başarısız.'),
    });
  }
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
