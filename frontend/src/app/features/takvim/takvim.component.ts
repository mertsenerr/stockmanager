import { HttpErrorResponse } from '@angular/common/http';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Calendar, EventClickArg, EventDropArg } from '@fullcalendar/core';
import dayGridPlugin from '@fullcalendar/daygrid';
import timeGridPlugin from '@fullcalendar/timegrid';
import interactionPlugin from '@fullcalendar/interaction';
import trLocale from '@fullcalendar/core/locales/tr';
import { ModalComponent } from '../../shared/ui/modal/modal.component';
import { PageHeaderComponent } from '../../shared/ui/page-header/page-header.component';
import { ConfirmService } from '../../shared/ui/confirm/confirm.service';
import { ToastService } from '../../shared/ui/toast/toast.service';
import { AuthService } from '../../core/auth/auth.service';
import { AtamaService } from './atama.service';
import {
  ATAMA_DURUM_LABELS,
  Atama,
  AtamaDurum,
  AtamaUpsert,
  colorForFirma,
} from './takvim.models';
import { Firma, KullaniciList, Magaza } from '../admin/admin.models';
import { FirmaService } from '../admin/firma.service';
import { MagazaService } from '../admin/magaza.service';
import { KullaniciService } from '../admin/kullanici.service';
import { SelectComponent, SelectOption } from '../../shared/ui/select/select.component';

@Component({
  selector: 'app-takvim',
  standalone: true,
  imports: [ReactiveFormsModule, ModalComponent, PageHeaderComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './takvim.component.html',
})
export class TakvimComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly svc = inject(AtamaService);
  private readonly fSvc = inject(FirmaService);
  private readonly mSvc = inject(MagazaService);
  private readonly uSvc = inject(KullaniciService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  @ViewChild('calEl') private calEl?: ElementRef<HTMLDivElement>;
  private calendar?: Calendar;

  readonly atamalar = signal<Atama[]>([]);
  readonly firmalar = signal<Firma[]>([]);
  readonly magazalar = signal<Magaza[]>([]);
  readonly yoneticiAdaylari = signal<KullaniciList[]>([]);
  readonly saymanAdaylari = signal<KullaniciList[]>([]);

  readonly selected = signal<Atama | null>(null);
  readonly modalOpen = signal(false);
  readonly editing = signal<Atama | null>(null);
  readonly saving = signal(false);
  readonly serverError = signal<string | null>(null);

  readonly firmaFilter = signal<string>('');
  readonly onlyMine = signal(false);

  readonly isAdmin = computed(() => {
    const r = this.auth.currentUser()?.rol;
    return r === 'Sistem' || r === 'SayimBaskani';
  });
  readonly currentUserId = computed(() => this.auth.currentUser()?.id ?? null);

  readonly durumOptions: { value: AtamaDurum; label: string }[] = [
    { value: 'planlandi', label: 'Planlandı' },
    { value: 'tamamlandi', label: 'Tamamlandı' },
    { value: 'iptal', label: 'İptal' },
  ];
  readonly durumSelectOptions: SelectOption[] = this.durumOptions.map((d) => ({ value: d.value, label: d.label }));
  readonly firmaFilterOptions = computed<SelectOption[]>(() => [
    { value: '', label: 'Tüm firmalar' },
    ...this.firmalar().map((f) => ({ value: f.id, label: f.ad })),
  ]);
  readonly magazaSelectOptions = computed<SelectOption[]>(() => [
    { value: '', label: '— Seçin —' },
    ...this.magazalar().map((m) => ({ value: m.id, label: `${m.firmaAdi} · ${m.ad}` })),
  ]);
  readonly yoneticiSelectOptions = computed<SelectOption[]>(() => [
    { value: '', label: '— Seçin —' },
    ...this.yoneticiAdaylari().map((u) => ({ value: u.id, label: `${u.adSoyad} (${u.email})` })),
  ]);
  readonly durumLabel = (d: AtamaDurum) => ATAMA_DURUM_LABELS[d];

  readonly form = this.fb.nonNullable.group({
    magazaId: ['', [Validators.required]],
    tarih: ['', [Validators.required]],
    baslangicSaati: [''],
    bitisSaati: [''],
    yoneticiKullaniciId: ['', [Validators.required]],
    saymanKullaniciIds: this.fb.nonNullable.control<string[]>([]),
    notlar: [''],
    durum: ['planlandi' as AtamaDurum],
  });

  ngOnInit(): void {
    this.fSvc.list(false).subscribe((r) => this.firmalar.set(r));
    this.mSvc.list({ includeInactive: false }).subscribe((r) => this.magazalar.set(r));
    this.uSvc.list(false).subscribe((r) => {
      this.yoneticiAdaylari.set(r.filter((u) => u.rol === 'Sistem' || u.rol === 'SayimBaskani'));
      this.saymanAdaylari.set(r.filter((u) => u.rol === 'Kullanici' || u.rol === 'SayimBaskani'));
    });
  }

  ngAfterViewInit(): void {
    queueMicrotask(() => this.initCalendar());
  }

  ngOnDestroy(): void {
    this.calendar?.destroy();
  }

  private initCalendar(): void {
    if (!this.calEl) return;
    const isAdmin = this.isAdmin();
    this.calendar = new Calendar(this.calEl.nativeElement, {
      plugins: [dayGridPlugin, timeGridPlugin, interactionPlugin],
      initialView: 'dayGridMonth',
      locale: trLocale,
      firstDay: 1,
      headerToolbar: {
        left: 'prev,next today',
        center: 'title',
        right: 'dayGridMonth,timeGridWeek,timeGridDay',
      },
      buttonText: { today: 'Bugün', month: 'Ay', week: 'Hafta', day: 'Gün' },
      height: 'auto',
      editable: isAdmin,
      eventStartEditable: isAdmin,
      eventDurationEditable: false,
      events: (info, success) => this.fetchEvents(info.startStr.slice(0, 10), info.endStr.slice(0, 10), success),
      eventClick: (arg: EventClickArg) => this.onEventClick(arg),
      eventDrop: (arg: EventDropArg) => this.onEventDrop(arg),
    });
    this.calendar.render();
  }

  private fetchEvents(
    fromYmd: string,
    toYmd: string,
    success: (events: any[]) => void,
  ): void {
    this.svc.list(fromYmd, toYmd).subscribe({
      next: (atamalar) => {
        const filtered = this.applyFilters(atamalar);
        this.atamalar.set(filtered);
        success(filtered.map((a) => this.toEvent(a)));
      },
      error: () => {
        this.toast.error('Atamalar yüklenemedi.');
        success([]);
      },
    });
  }

  private applyFilters(list: Atama[]): Atama[] {
    const fid = this.firmaFilter();
    const mineOnly = this.onlyMine();
    const me = this.currentUserId();
    return list.filter((a) => {
      if (fid && a.firmaId !== fid) return false;
      if (mineOnly && me) {
        if (a.yoneticiKullaniciId !== me && !a.saymanKullaniciIds.includes(me)) return false;
      }
      return true;
    });
  }

  private toEvent(a: Atama): any {
    const dateYmd = a.tarih.slice(0, 10);
    const start = a.baslangicSaati ? `${dateYmd}T${a.baslangicSaati}` : dateYmd;
    const end = a.baslangicSaati && a.bitisSaati ? `${dateYmd}T${a.bitisSaati}` : undefined;
    const color = colorForFirma(a.firmaId);
    return {
      id: a.id,
      title: `${a.firmaAdi} · ${a.magazaAdi}`,
      start,
      end,
      allDay: !a.baslangicSaati,
      backgroundColor: color,
      borderColor: color,
      classNames: a.durum === 'iptal'
        ? ['opacity-50', 'line-through']
        : a.durum === 'tamamlandi'
          ? ['opacity-75']
          : [],
    };
  }

  refetch(): void {
    this.calendar?.refetchEvents();
  }

  setFirmaFilter(v: string): void {
    this.firmaFilter.set(v);
    this.refetch();
  }

  toggleMine(): void {
    this.onlyMine.update((v) => !v);
    this.refetch();
  }

  private onEventClick(arg: EventClickArg): void {
    const id = arg.event.id;
    const atama = this.atamalar().find((a) => a.id === id);
    if (atama) this.selected.set(atama);
  }

  private onEventDrop(arg: EventDropArg): void {
    if (!this.isAdmin()) {
      arg.revert();
      return;
    }
    const id = arg.event.id;
    const newDate = arg.event.startStr.slice(0, 10);
    this.svc.moveDate(id, newDate).subscribe({
      next: () => {
        this.toast.success('Atama tarihi güncellendi.');
        this.refetch();
      },
      error: (err: HttpErrorResponse) => {
        arg.revert();
        this.toast.error(err.error?.message ?? 'Tarih güncellenemedi.');
      },
    });
  }

  closeDetail(): void {
    this.selected.set(null);
  }

  openCreate(): void {
    if (!this.isAdmin()) return;
    this.editing.set(null);
    this.serverError.set(null);
    this.form.reset({
      magazaId: '', tarih: this.todayYmd(), baslangicSaati: '', bitisSaati: '',
      yoneticiKullaniciId: '', saymanKullaniciIds: [], notlar: '', durum: 'planlandi',
    });
    this.modalOpen.set(true);
  }

  openEditFromSelected(): void {
    const a = this.selected();
    if (!a || !this.isAdmin()) return;
    this.editing.set(a);
    this.serverError.set(null);
    this.form.reset({
      magazaId: a.magazaId,
      tarih: a.tarih.slice(0, 10),
      baslangicSaati: a.baslangicSaati ?? '',
      bitisSaati: a.bitisSaati ?? '',
      yoneticiKullaniciId: a.yoneticiKullaniciId,
      saymanKullaniciIds: [...a.saymanKullaniciIds],
      notlar: a.notlar ?? '',
      durum: a.durum,
    });
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
  }

  toggleSayman(id: string): void {
    const ctrl = this.form.controls.saymanKullaniciIds;
    const cur = ctrl.value;
    ctrl.setValue(cur.includes(id) ? cur.filter((x) => x !== id) : [...cur, id]);
  }

  isSaymanChecked(id: string): boolean {
    return this.form.controls.saymanKullaniciIds.value.includes(id);
  }

  submit(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const payload: AtamaUpsert = {
      magazaId: v.magazaId,
      tarih: v.tarih,
      baslangicSaati: v.baslangicSaati || null,
      bitisSaati: v.bitisSaati || null,
      yoneticiKullaniciId: v.yoneticiKullaniciId,
      saymanKullaniciIds: v.saymanKullaniciIds,
      notlar: v.notlar.trim() || null,
      durum: v.durum,
    };
    this.saving.set(true);
    this.serverError.set(null);

    const editing = this.editing();
    const op = editing
      ? this.svc.update(editing.id, payload)
      : this.svc.create(payload);

    op.subscribe({
      next: () => {
        this.saving.set(false);
        this.modalOpen.set(false);
        this.toast.success(editing ? 'Atama güncellendi.' : 'Atama oluşturuldu.');
        this.selected.set(null);
        this.refetch();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.serverError.set(err.error?.message ?? 'İşlem başarısız.');
      },
    });
  }

  async removeSelected(): Promise<void> {
    const a = this.selected();
    if (!a || !this.isAdmin()) return;
    const ok = await this.confirm.ask({
      title: 'Atamayı sil',
      message: `${a.firmaAdi} · ${a.magazaAdi} (${this.formatDate(a.tarih)}) silinsin mi?`,
      confirmLabel: 'Sil',
      danger: true,
    });
    if (!ok) return;
    this.svc.remove(a.id).subscribe({
      next: () => {
        this.toast.success('Atama silindi.');
        this.selected.set(null);
        this.refetch();
      },
      error: () => this.toast.error('Silme başarısız.'),
    });
  }

  formatDate(iso: string): string {
    return iso.slice(0, 10);
  }

  private todayYmd(): string {
    const d = new Date();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${d.getFullYear()}-${m}-${day}`;
  }
}
