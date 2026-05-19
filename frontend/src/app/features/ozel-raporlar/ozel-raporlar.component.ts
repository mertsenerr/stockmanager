import { ChangeDetectionStrategy, Component, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { ModalComponent } from '../../shared/ui/modal/modal.component';
import { PageHeaderComponent } from '../../shared/ui/page-header/page-header.component';
import { ConfirmService } from '../../shared/ui/confirm/confirm.service';
import { ToastService } from '../../shared/ui/toast/toast.service';
import { AuthService } from '../../core/auth/auth.service';
import { ArkadasService, Friend } from '../arkadaslar/arkadas.service';
import { OzelRaporService } from './ozel-rapor.service';
import { OzelRapor, OzelRaporDosya } from './ozel-rapor.models';
import { BelgeTipiService } from '../belge-tipleri/belge-tipi.service';
import { BelgeTipi, IMZA_ROL_OPTIONS } from '../belge-tipleri/belge-tipi.models';
import { SignaturePadComponent } from './signature-pad.component';

const MAX_FILE_SIZE = 50 * 1024 * 1024;
const ALLOWED_EXT = ['.xlsx', '.xls', '.pdf'];

@Component({
  selector: 'app-ozel-raporlar',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ModalComponent, PageHeaderComponent, SignaturePadComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ozel-raporlar.component.html',
})
export class OzelRaporlarComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly svc = inject(OzelRaporService);
  private readonly arkadasSvc = inject(ArkadasService);
  private readonly belgeTipiSvc = inject(BelgeTipiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly user = this.auth.currentUser;
  readonly canManage = computed(() => {
    const r = this.user()?.rol;
    return r === 'Sistem' || r === 'SayimBaskani';
  });

  readonly raporlar = signal<OzelRapor[]>([]);
  readonly loading = signal(false);
  readonly query = signal('');

  readonly editing = signal<OzelRapor | null>(null);
  readonly modalOpen = signal(false);
  readonly saving = signal(false);
  readonly serverError = signal<string | null>(null);
  readonly uploading = signal<string | null>(null);
  readonly downloadingId = signal<string | null>(null);

  /** Aktif belge tipleri — yükleme dropdown'unda gösterilir. */
  readonly belgeTipleri = signal<BelgeTipi[]>([]);
  /** Her rapor için seçilmiş belge tipi id'si (rapor-id → belge-tipi-id). */
  readonly selectedBelgeTipi = signal<Record<string, string>>({});

  /** Mevcut arkadaşlar (erişim verilebilecek havuz). */
  readonly friends = signal<Friend[]>([]);
  /** Modal'da seçilmiş arkadaş id'leri (User.Id). */
  readonly selectedFriendIds = signal<string[]>([]);
  readonly friendQuery = signal('');

  readonly form = this.fb.nonNullable.group({
    ad: ['', [Validators.required, Validators.maxLength(160)]],
  });

  readonly filtered = computed(() => {
    const q = this.query().toLowerCase().trim();
    return q === ''
      ? this.raporlar()
      : this.raporlar().filter((r) => r.ad.toLowerCase().includes(q));
  });

  /** Bulk-select state. */
  readonly selectedIds = signal<Set<string>>(new Set());
  readonly bulkDeleting = signal(false);

  readonly allFilteredSelected = computed(() => {
    const f = this.filtered().filter((r) => r.duzenleyebilir);
    if (f.length === 0) return false;
    const sel = this.selectedIds();
    return f.every((r) => sel.has(r.id));
  });

  isSelected(id: string): boolean { return this.selectedIds().has(id); }

  toggleSelect(id: string, event?: Event): void {
    event?.stopPropagation();
    this.selectedIds.update((s) => {
      const next = new Set(s);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }

  toggleSelectAll(): void {
    const all = this.allFilteredSelected();
    this.selectedIds.update((s) => {
      const next = new Set(s);
      const ids = this.filtered().filter((r) => r.duzenleyebilir).map((r) => r.id);
      if (all) {
        for (const id of ids) next.delete(id);
      } else {
        for (const id of ids) next.add(id);
      }
      return next;
    });
  }

  clearSelection(): void { this.selectedIds.set(new Set()); }

  async deleteSelected(): Promise<void> {
    if (this.bulkDeleting()) return;
    const ids = Array.from(this.selectedIds());
    if (ids.length === 0) return;
    const ok = await this.confirm.ask({
      title: `${ids.length} rapor silinsin mi?`,
      message: `Seçilen ${ids.length} özel rapor ve içindeki tüm dosyalar kalıcı olarak silinecek. Bu işlem geri alınamaz.`,
      confirmLabel: 'Kalıcı sil',
      cancelLabel: 'Vazgeç',
      danger: true,
    });
    if (!ok) return;
    this.bulkDeleting.set(true);
    let success = 0;
    let failure = 0;
    await Promise.all(ids.map((id) => new Promise<void>((resolve) => {
      this.svc.remove(id).subscribe({
        next: () => { success++; resolve(); },
        error: () => { failure++; resolve(); },
      });
    })));
    this.bulkDeleting.set(false);
    this.clearSelection();
    this.refresh();
    if (failure === 0) this.toast.success(`${success} rapor silindi.`);
    else this.toast.error(`${success} silindi, ${failure} başarısız.`);
  }

  readonly selectedFriends = computed(() => {
    const ids = new Set(this.selectedFriendIds());
    return this.friends().filter((f) => ids.has(f.kullaniciId));
  });

  readonly suggestedFriends = computed(() => {
    const q = this.friendQuery().toLowerCase().trim();
    const selected = new Set(this.selectedFriendIds());
    const pool = this.friends().filter((f) => !selected.has(f.kullaniciId));
    if (q === '') return pool.slice(0, 8);
    return pool
      .filter((f) =>
        f.adSoyad.toLowerCase().includes(q) || f.email.toLowerCase().includes(q),
      )
      .slice(0, 8);
  });

  ngOnInit(): void {
    this.refresh();
    if (this.canManage()) {
      this.arkadasSvc.list().subscribe({
        next: (r) => this.friends.set(r.arkadaslar),
        error: () => undefined,
      });
      this.belgeTipiSvc.list().subscribe({
        next: (list) => this.belgeTipleri.set(list.filter((t) => !t.arsivlendi)),
        error: () => undefined,
      });
    }
  }

  setBelgeTipi(raporId: string, value: string): void {
    this.selectedBelgeTipi.update((m) => ({ ...m, [raporId]: value }));
  }

  getBelgeTipi(raporId: string): string {
    return this.selectedBelgeTipi()[raporId] ?? '';
  }

  imzaLabel(roller: string[]): string {
    if (!roller || roller.length === 0) return '';
    return roller
      .map((r) => IMZA_ROL_OPTIONS.find((o) => o.value === r)?.label ?? r)
      .join(', ');
  }

  rolEtiketi(rol: string): string {
    return IMZA_ROL_OPTIONS.find((o) => o.value === rol)?.label ?? rol;
  }

  // ─── İmza / Kaşe akışı ──────────────────────────────────────────────────
  @ViewChild(SignaturePadComponent) private signaturePad?: SignaturePadComponent;
  readonly signModalOpen = signal(false);
  readonly signTarget = signal<{ raporId: string; dosya: OzelRaporDosya; rol: string } | null>(null);
  readonly signSaving = signal(false);
  readonly signPadEmpty = signal(true);
  readonly signedDownloadId = signal<string | null>(null);

  /** Bu rolün imzasını bu kullanıcı atabilir mi? */
  canSignAs(rol: string): boolean {
    const u = this.user();
    if (!u) return false;
    if (u.rol === 'Sistem') return true;
    if (rol === 'SayimBaskani') return u.rol === 'SayimBaskani';
    if (rol === 'MagazaYetkilisi') return u.rol === 'Kullanici';
    return false;
  }

  /** Bu kullanıcı bu dosyada kaşe basabilir mi? (Mağaza yetkilisi veya Sistem) */
  canStamp(d: OzelRaporDosya): boolean {
    if (!d.kaseGerekli) return false;
    if (d.kase) return false;
    const u = this.user();
    if (!u) return false;
    return u.rol === 'Sistem' || u.rol === 'Kullanici';
  }

  /** Belirli rolün imzası bu dosyada mevcut mu? */
  hasSignature(d: OzelRaporDosya, rol: string): boolean {
    return d.imzalar.some((i) => i.rol === rol);
  }

  getSignature(d: OzelRaporDosya, rol: string) {
    return d.imzalar.find((i) => i.rol === rol);
  }

  isPdf(d: OzelRaporDosya): boolean {
    return d.ad.toLowerCase().endsWith('.pdf');
  }

  openSignModal(raporId: string, dosya: OzelRaporDosya, rol: string): void {
    this.signTarget.set({ raporId, dosya, rol });
    this.signPadEmpty.set(true);
    this.signModalOpen.set(true);
    setTimeout(() => this.signaturePad?.clear(), 0);
  }

  closeSignModal(): void {
    this.signModalOpen.set(false);
    this.signTarget.set(null);
  }

  clearPad(): void {
    this.signaturePad?.clear();
    this.signPadEmpty.set(true);
  }

  onPadChanged(hasInk: boolean): void { this.signPadEmpty.set(!hasInk); }

  saveSignature(): void {
    const target = this.signTarget();
    if (!target || this.signSaving()) return;
    const dataUri = this.signaturePad?.toDataUri();
    if (!dataUri) {
      this.toast.error('İmza boş.');
      return;
    }
    this.signSaving.set(true);
    this.svc.imzaAt(target.raporId, target.dosya.id, target.rol, dataUri).subscribe({
      next: (saved) => {
        this.signSaving.set(false);
        this.toast.success('İmza atıldı.');
        this.replaceRapor(saved as OzelRapor);
        this.closeSignModal();
      },
      error: (err: HttpErrorResponse) => {
        this.signSaving.set(false);
        this.toast.error(err.error?.message ?? 'İmza başarısız.');
      },
    });
  }

  removeSignature(r: OzelRapor, d: OzelRaporDosya, imzaId: string): void {
    this.svc.imzaSil(r.id, d.id, imzaId).subscribe({
      next: () => {
        this.toast.success('İmza silindi.');
        // Yerel state'i güncelle — backend full rapor dönmüyor (NoContent).
        this.raporlar.set(this.raporlar().map((x) =>
          x.id !== r.id ? x : {
            ...x,
            dosyalar: x.dosyalar.map((f) =>
              f.id !== d.id ? f : { ...f, imzalar: f.imzalar.filter((i) => i.id !== imzaId) }),
          },
        ));
      },
      error: (err: HttpErrorResponse) =>
        this.toast.error(err.error?.message ?? 'İmza silinemedi.'),
    });
  }

  stamp(r: OzelRapor, d: OzelRaporDosya): void {
    this.svc.kaseBas(r.id, d.id).subscribe({
      next: (saved) => {
        this.toast.success('Kaşe basıldı.');
        this.replaceRapor(saved as OzelRapor);
      },
      error: (err: HttpErrorResponse) =>
        this.toast.error(err.error?.message ?? 'Kaşe başarısız.'),
    });
  }

  removeStamp(r: OzelRapor, d: OzelRaporDosya): void {
    this.svc.kaseSil(r.id, d.id).subscribe({
      next: () => {
        this.toast.success('Kaşe silindi.');
        this.raporlar.set(this.raporlar().map((x) =>
          x.id !== r.id ? x : {
            ...x,
            dosyalar: x.dosyalar.map((f) =>
              f.id !== d.id ? f : { ...f, kase: null }),
          },
        ));
      },
      error: (err: HttpErrorResponse) =>
        this.toast.error(err.error?.message ?? 'Kaşe silinemedi.'),
    });
  }

  async downloadSigned(r: OzelRapor, d: OzelRaporDosya): Promise<void> {
    this.signedDownloadId.set(d.id);
    try {
      const stem = d.ad.replace(/\.[^.]+$/, '');
      await this.svc.downloadSigned(r.id, d.id, `${stem} (imzalı).pdf`);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'İndirme başarısız';
      this.toast.error(msg);
    } finally {
      this.signedDownloadId.set(null);
    }
  }

  private replaceRapor(saved: OzelRapor): void {
    this.raporlar.set(this.raporlar().map((x) => (x.id === saved.id ? saved : x)));
  }

  refresh(): void {
    this.loading.set(true);
    this.svc.list().subscribe({
      next: (r) => { this.raporlar.set(r); this.loading.set(false); },
      error: () => { this.loading.set(false); this.toast.error('Raporlar yüklenemedi.'); },
    });
  }

  openCreate(): void {
    this.editing.set(null);
    this.serverError.set(null);
    this.form.reset({ ad: '' });
    this.selectedFriendIds.set([]);
    this.friendQuery.set('');
    this.modalOpen.set(true);
  }

  openEdit(r: OzelRapor): void {
    this.editing.set(r);
    this.serverError.set(null);
    this.form.reset({ ad: r.ad });
    this.selectedFriendIds.set([...r.erisebilenKullaniciIds]);
    this.friendQuery.set('');
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
  }

  addFriend(f: Friend): void {
    const cur = this.selectedFriendIds();
    if (cur.includes(f.kullaniciId)) return;
    this.selectedFriendIds.set([...cur, f.kullaniciId]);
    this.friendQuery.set('');
  }

  removeFriend(uid: string): void {
    this.selectedFriendIds.set(this.selectedFriendIds().filter((x) => x !== uid));
  }

  submit(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.serverError.set(null);
    const v = this.form.getRawValue();
    const payload = {
      ad: v.ad.trim(),
      aciklama: null,
      erisebilenKullaniciIds: this.selectedFriendIds(),
    };
    const editing = this.editing();
    const op = editing ? this.svc.update(editing.id, payload) : this.svc.create(payload);
    op.subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.modalOpen.set(false);
        this.toast.success(editing ? 'Rapor güncellendi.' : 'Rapor oluşturuldu.');
        const list = this.raporlar();
        if (editing) {
          this.raporlar.set(list.map((r) => (r.id === saved.id ? saved : r)));
        } else {
          this.raporlar.set([saved, ...list]);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.serverError.set(err.error?.message ?? 'İşlem başarısız.');
      },
    });
  }

  async remove(r: OzelRapor): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Raporu sil',
      message: `"${r.ad}" ve içindeki tüm dosyalar kalıcı olarak silinecek. Devam edilsin mi?`,
      confirmLabel: 'Sil',
      danger: true,
    });
    if (!ok) return;
    this.svc.remove(r.id).subscribe({
      next: () => {
        this.toast.success('Rapor silindi.');
        this.raporlar.set(this.raporlar().filter((x) => x.id !== r.id));
      },
      error: (err: HttpErrorResponse) =>
        this.toast.error(err.error?.message ?? 'Silme başarısız.'),
    });
  }

  triggerUpload(r: OzelRapor, input: HTMLInputElement): void {
    input.value = '';
    input.click();
  }

  onFilesChosen(r: OzelRapor, ev: Event): void {
    const input = ev.target as HTMLInputElement;
    const list = input.files;
    if (!list || list.length === 0) return;

    const files: File[] = [];
    for (let i = 0; i < list.length; i++) {
      const f = list.item(i);
      if (!f) continue;
      const ext = f.name.toLowerCase().slice(f.name.lastIndexOf('.'));
      if (!ALLOWED_EXT.includes(ext)) {
        this.toast.error(`${f.name}: sadece ${ALLOWED_EXT.join(', ')} yüklenebilir.`);
        return;
      }
      if (f.size > MAX_FILE_SIZE) {
        this.toast.error(`${f.name}: 50 MB sınırını aşıyor.`);
        return;
      }
      files.push(f);
    }
    if (files.length === 0) return;

    this.uploading.set(r.id);
    const belgeTipiId = this.getBelgeTipi(r.id) || null;
    this.svc.uploadFiles(r.id, files, belgeTipiId).subscribe({
      next: (saved) => {
        this.uploading.set(null);
        this.toast.success(`${files.length} dosya yüklendi.`);
        this.raporlar.set(this.raporlar().map((x) => (x.id === saved.id ? saved : x)));
      },
      error: (err: HttpErrorResponse) => {
        this.uploading.set(null);
        this.toast.error(err.error?.message ?? 'Yükleme başarısız.');
      },
    });
  }

  async removeFile(r: OzelRapor, d: OzelRaporDosya): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Dosyayı sil',
      message: `"${d.ad}" silinsin mi?`,
      confirmLabel: 'Sil',
      danger: true,
    });
    if (!ok) return;
    this.svc.removeFile(r.id, d.id).subscribe({
      next: () => {
        this.toast.success('Dosya silindi.');
        this.raporlar.set(
          this.raporlar().map((x) =>
            x.id === r.id ? { ...x, dosyalar: x.dosyalar.filter((f) => f.id !== d.id) } : x,
          ),
        );
      },
      error: (err: HttpErrorResponse) =>
        this.toast.error(err.error?.message ?? 'Silme başarısız.'),
    });
  }

  async download(r: OzelRapor, d: OzelRaporDosya): Promise<void> {
    this.downloadingId.set(d.id);
    try {
      await this.svc.download(r.id, d.id, d.ad);
    } catch {
      this.toast.error('İndirme başarısız.');
    } finally {
      this.downloadingId.set(null);
    }
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  formatDate(iso: string | null | undefined): string {
    if (!iso) return '—';
    return new Date(iso).toLocaleString('tr-TR');
  }
}
