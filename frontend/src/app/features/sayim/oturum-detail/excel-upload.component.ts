import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import * as XLSX from 'xlsx';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import {
  ExcelImportPayload,
  ExcelImportRow,
  ExcelMapping,
  FIRMA_TIP_PROFILI,
  ProfilKolonAnahtari,
  ProfilKolonu,
} from '../sayim.models';
import { FIRMA_TIP_LABELS, FirmaTip } from '../../admin/admin.models';

interface ParsedSheet {
  fileName: string;
  headers: string[];
  rows: Record<string, unknown>[];
}

type SabitAlan = 'barkod' | 'urunAdi' | 'sistemStok' | 'sayilanStok' | 'fiyat';

type Detection = Record<SabitAlan | ProfilKolonAnahtari, string>;
type DetectionSource = Record<SabitAlan | ProfilKolonAnahtari, '' | 'header' | 'content' | 'manual'>;

const MAX_BYTES = 5 * 1024 * 1024; // 5 MB
const SNIFF_ROWS = 25;

const SABIT_KEYWORDS: Record<SabitAlan, string[]> = {
  barkod: ['barkod', 'barcode', 'bar code', 'ean', 'gtin', 'item code'],
  urunAdi: [
    'ürün adı', 'urun adi', 'ürün ad', 'urun ad', 'ürün', 'urun',
    'product name', 'product', 'description', 'açıklama', 'aciklama',
    'item name', 'isim', 'ad',
  ],
  sistemStok: [
    'sistem stok', 'sistem stoku', 'sistem stoğu',
    'kayıt stok', 'kayit stok', 'kayıtlı stok', 'kayitli stok',
    'defter stok', 'beklenen stok', 'theoretical', 'theoric',
    'expected', 'recorded', 'mevcut stok', 'sistem',
  ],
  sayilanStok: [
    'fiili stok', 'fiili', 'fiziki stok', 'fiziksel stok',
    'sayılan stok', 'sayilan stok', 'gerçek stok', 'gercek stok',
    'sayım sonucu', 'sayim sonucu', 'sayılan', 'sayilan',
    'fiziki', 'fiziksel', 'counted', 'actual', 'count',
    'sayım', 'sayim',
  ],
  fiyat: [
    'birim fiyat', 'satış fiyatı', 'satis fiyati', 'satış fiyat', 'satis fiyat',
    'fiyat', 'price', 'unit price', 'tutar',
  ],
};

const SABIT_LABELS: Record<SabitAlan, string> = {
  barkod: 'Barkod',
  urunAdi: 'Ürün adı',
  sistemStok: 'Sistem',
  sayilanStok: 'Fiili',
  fiyat: 'Fiyat',
};

const EMPTY_DETECTION: Detection = {
  barkod: '', urunAdi: '', sistemStok: '', sayilanStok: '', fiyat: '',
  stokKodu: '', kategori: '', altKategori: '', renk: '', beden: '', marka: '', model: '',
};
const EMPTY_SOURCE: DetectionSource = {
  barkod: '', urunAdi: '', sistemStok: '', sayilanStok: '', fiyat: '',
  stokKodu: '', kategori: '', altKategori: '', renk: '', beden: '', marka: '', model: '',
};

interface PreviewRow {
  sabit: Record<SabitAlan, string | number>;
  profil: Partial<Record<ProfilKolonAnahtari, string>>;
  sistemFarki: number | null;
  fiiliFarki: number | null;
  fiyatFarki: number | null;
}

@Component({
  selector: 'app-excel-upload',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './excel-upload.component.html',
  styleUrl: './excel-upload.component.css',
})
export class ExcelUploadComponent {
  private readonly toast = inject(ToastService);

  @Input() uploading = false;
  @Input() set firmaTip(value: FirmaTip | null | undefined) {
    this._firmaTip.set(value ?? 'diger');
  }
  @Output() readonly importReady = new EventEmitter<ExcelImportPayload>();

  private readonly _firmaTip = signal<FirmaTip>('diger');
  readonly firmaTipLabel = computed(() => FIRMA_TIP_LABELS[this._firmaTip()]);

  readonly profil = computed<ProfilKolonu[]>(() => FIRMA_TIP_PROFILI[this._firmaTip()] ?? []);
  readonly profilEtiketleri = computed(() => this.profil().map((k) => k.label).join(', '));

  readonly parsed = signal<ParsedSheet | null>(null);
  readonly dragging = signal(false);
  readonly mappingError = signal<string | null>(null);
  readonly autoDetected = signal(false);
  readonly manualEdit = signal(false);
  readonly detection = signal<Detection>({ ...EMPTY_DETECTION });
  readonly detectionSource = signal<DetectionSource>({ ...EMPTY_SOURCE });
  readonly preview = signal<PreviewRow[]>([]);

  // Manual mapping state — bound to selects in manual mode.
  manual: Detection = { ...EMPTY_DETECTION };

  readonly sabitAlanlar: SabitAlan[] = ['barkod', 'urunAdi', 'sistemStok', 'sayilanStok', 'fiyat'];
  readonly sabitLabel = (a: SabitAlan) => SABIT_LABELS[a];

  sourceLabel(s: string): string {
    return s === 'header' ? 'BAŞLIK' : s === 'content' ? 'İÇERİK' : s === 'manual' ? 'EL İLE' : '—';
  }

  onDragOver(e: DragEvent): void {
    e.preventDefault();
    this.dragging.set(true);
  }

  onDrop(e: DragEvent): void {
    e.preventDefault();
    this.dragging.set(false);
    const file = e.dataTransfer?.files?.[0];
    if (file) this.handleFile(file);
  }

  onFileInput(e: Event): void {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.handleFile(file);
  }

  private async handleFile(file: File): Promise<void> {
    if (file.size > MAX_BYTES) {
      this.toast.error('Dosya 5 MB üst sınırını aşıyor.');
      return;
    }
    if (!/\.(xlsx|xls)$/i.test(file.name)) {
      this.toast.error('Sadece .xlsx veya .xls dosyaları desteklenir.');
      return;
    }
    try {
      const buf = await file.arrayBuffer();
      const wb = XLSX.read(buf, { type: 'array' });
      const sheetName = wb.SheetNames[0];
      if (!sheetName) {
        this.toast.error('Excel dosyasında sayfa yok.');
        return;
      }
      const sheet = wb.Sheets[sheetName];
      const rows = XLSX.utils.sheet_to_json<Record<string, unknown>>(sheet, { defval: '' });
      if (rows.length === 0) {
        this.toast.error('Dosya boş.');
        return;
      }
      const headers = Object.keys(rows[0]);
      this.parsed.set({ fileName: file.name, headers, rows });

      const { detection, source } = this.detectMapping(headers, rows);
      this.detection.set(detection);
      this.detectionSource.set(source);
      this.manual = { ...detection };

      const isHighConfidence = !!detection.barkod && (!!detection.sistemStok || !!detection.sayilanStok);
      this.autoDetected.set(isHighConfidence);
      this.manualEdit.set(!isHighConfidence);

      this.recomputePreview();

      if (isHighConfidence) {
        this.toast.success('Excel tarandı — kolonlar otomatik eşlendi.');
      } else {
        this.toast.info('Otomatik eşleme kesinleştiremedi, kolonları kontrol et.');
      }
    } catch {
      this.toast.error('Excel okunamadı.');
    }
  }

  /** Smart auto-detection — sabit + tip profil kolonları. */
  private detectMapping(
    headers: string[],
    rows: Record<string, unknown>[],
  ): { detection: Detection; source: DetectionSource } {
    const lower = headers.map((h) => h.toLowerCase().trim());
    const detection: Detection = { ...EMPTY_DETECTION };
    const source: DetectionSource = { ...EMPTY_SOURCE };
    const used = new Set<string>();

    const matchHeader = (keywords: string[]): string | null => {
      const sorted = [...keywords].sort((a, b) => b.length - a.length);
      for (const kw of sorted) {
        for (let i = 0; i < lower.length; i++) {
          if (used.has(headers[i])) continue;
          if (lower[i].includes(kw)) return headers[i];
        }
      }
      return null;
    };

    // 1. Sabit alanlar — header eşleştirmesi
    for (const alan of this.sabitAlanlar) {
      const m = matchHeader(SABIT_KEYWORDS[alan]);
      if (m) {
        detection[alan] = m;
        source[alan] = 'header';
        used.add(m);
      }
    }

    // 2. Profil alanları — header eşleştirmesi
    for (const k of this.profil()) {
      const m = matchHeader(k.keywords);
      if (m) {
        detection[k.key] = m;
        source[k.key] = 'header';
        used.add(m);
      }
    }

    // 3. Content sniffing — barkod, sayısal alanlar için fallback
    const stats = headers.map((h) => this.profileColumn(rows, h));

    if (!detection.barkod) {
      const candidate = headers
        .map((h, i) => ({ h, s: stats[i] }))
        .filter((x) => !used.has(x.h))
        .filter((x) => x.s.barcodeLikeRatio >= 0.7 && x.s.uniqueRatio >= 0.7)
        .sort((a, b) => b.s.barcodeLikeRatio - a.s.barcodeLikeRatio)[0];
      if (candidate) {
        detection.barkod = candidate.h;
        source.barkod = 'content';
        used.add(candidate.h);
      }
    }

    const numericCols = headers
      .map((h, i) => ({ h, s: stats[i] }))
      .filter((x) => !used.has(x.h))
      .filter((x) => x.s.numericRatio >= 0.8 && x.s.nonEmptyRatio >= 0.5);

    if (!detection.sistemStok && !detection.sayilanStok && numericCols.length >= 2) {
      detection.sistemStok = numericCols[0].h;
      detection.sayilanStok = numericCols[1].h;
      source.sistemStok = 'content';
      source.sayilanStok = 'content';
      used.add(numericCols[0].h);
      used.add(numericCols[1].h);
    } else if (!detection.sistemStok && numericCols.length >= 1) {
      detection.sistemStok = numericCols[0].h;
      source.sistemStok = 'content';
      used.add(numericCols[0].h);
    } else if (!detection.sayilanStok && numericCols.length >= 1) {
      const remaining = numericCols.find((x) => !used.has(x.h));
      if (remaining) {
        detection.sayilanStok = remaining.h;
        source.sayilanStok = 'content';
        used.add(remaining.h);
      }
    }

    if (!detection.urunAdi) {
      const candidate = headers
        .map((h, i) => ({ h, s: stats[i] }))
        .filter((x) => !used.has(x.h))
        .filter((x) => x.s.avgTextLen >= 6 && x.s.numericRatio < 0.5)
        .sort((a, b) => b.s.avgTextLen - a.s.avgTextLen)[0];
      if (candidate) {
        detection.urunAdi = candidate.h;
        source.urunAdi = 'content';
      }
    }

    return { detection, source };
  }

  private profileColumn(rows: Record<string, unknown>[], header: string) {
    const sample = rows.slice(0, SNIFF_ROWS);
    let nonEmpty = 0, numeric = 0, barcodeLike = 0, totalLen = 0;
    const seen = new Set<string>();
    for (const r of sample) {
      const v = r[header];
      const s = String(v ?? '').trim();
      if (s === '') continue;
      nonEmpty++;
      seen.add(s);
      totalLen += s.length;
      const cleaned = s.replace(/[\s.,]/g, '');
      if (cleaned !== '' && /^-?\d+(\.\d+)?$/.test(s.replace(',', '.'))) numeric++;
      if (/^[0-9A-Za-z\-]{6,18}$/.test(s) && /\d/.test(s)) barcodeLike++;
    }
    const total = Math.max(1, sample.length);
    return {
      nonEmptyRatio: nonEmpty / total,
      numericRatio: nonEmpty === 0 ? 0 : numeric / nonEmpty,
      barcodeLikeRatio: nonEmpty === 0 ? 0 : barcodeLike / nonEmpty,
      avgTextLen: nonEmpty === 0 ? 0 : totalLen / nonEmpty,
      uniqueRatio: nonEmpty === 0 ? 0 : seen.size / nonEmpty,
    };
  }

  private recomputePreview(): void {
    const p = this.parsed();
    const det = this.detection();
    if (!p || !det.barkod) {
      this.preview.set([]);
      return;
    }
    this.preview.set(p.rows.slice(0, 5).map((r) => this.toPreviewRow(r, det)));
  }

  private toPreviewRow(r: Record<string, unknown>, det: Detection): PreviewRow {
    const sistem = this.toNumber(r[det.sistemStok]);
    const fiili = this.toNumber(r[det.sayilanStok]);
    const fiyat = det.fiyat ? this.toNumber(r[det.fiyat]) : null;
    const sabit: Record<SabitAlan, string | number> = {
      barkod: String(r[det.barkod] ?? '').trim(),
      urunAdi: String(r[det.urunAdi] ?? '').trim(),
      sistemStok: sistem,
      sayilanStok: fiili,
      fiyat: fiyat ?? 0,
    };
    const profil: Partial<Record<ProfilKolonAnahtari, string>> = {};
    for (const k of this.profil()) {
      const col = det[k.key];
      if (col) profil[k.key] = String(r[col] ?? '').trim();
    }
    const sistemFarki = fiyat !== null ? sistem * fiyat : null;
    const fiiliFarki = fiyat !== null ? fiili * fiyat : null;
    const fiyatFarki = fiyat !== null ? (fiili - sistem) * fiyat : null;
    return { sabit, profil, sistemFarki, fiiliFarki, fiyatFarki };
  }

  ngDoCheck(): void {
    if (!this.parsed()) return;
    // Sync manual edits → detection signal so preview reflects them.
    const cur = this.detection();
    let dirty = false;
    const next: Detection = { ...cur };
    for (const key of Object.keys(cur) as (keyof Detection)[]) {
      if (this.manual[key] !== cur[key]) {
        next[key] = this.manual[key];
        dirty = true;
      }
    }
    if (dirty) {
      this.detection.set(next);
      // any manual change overrides the source flag
      const src = { ...this.detectionSource() };
      for (const key of Object.keys(cur) as (keyof Detection)[]) {
        if (this.manual[key] && next[key] && next[key] !== cur[key]) src[key] = 'manual';
      }
      this.detectionSource.set(src);
    }
    this.recomputePreview();
  }

  private toNumber(v: unknown): number {
    if (typeof v === 'number') return v;
    const n = Number(String(v ?? '').replace(',', '.'));
    return Number.isFinite(n) ? n : 0;
  }

  reset(): void {
    this.parsed.set(null);
    this.preview.set([]);
    this.autoDetected.set(false);
    this.manualEdit.set(false);
    this.detection.set({ ...EMPTY_DETECTION });
    this.detectionSource.set({ ...EMPTY_SOURCE });
    this.manual = { ...EMPTY_DETECTION };
    this.mappingError.set(null);
  }

  switchToManual(): void {
    this.manualEdit.set(true);
    this.autoDetected.set(false);
  }

  formatTl(v: number | null): string {
    if (v === null) return '—';
    return v.toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' ₺';
  }

  upload(): void {
    const p = this.parsed();
    if (!p) return;
    const det = this.detection();
    this.mappingError.set(null);
    if (!det.barkod) {
      this.mappingError.set('Barkod kolonu zorunludur — el ile düzelt.');
      this.autoDetected.set(false);
      this.manualEdit.set(true);
      return;
    }

    const all: ExcelImportRow[] = p.rows.map((r) => this.toRow(r, det))
      .filter((r) => r.barkod);
    if (all.length === 0) {
      this.mappingError.set('Geçerli barkod içeren satır yok.');
      return;
    }
    if (all.length > 50000) {
      this.mappingError.set(`Tek seferde 50.000 satırdan fazla yüklenemez (${all.length}).`);
      return;
    }

    const mapping: ExcelMapping = {
      barkodKolon: det.barkod,
      urunAdiKolon: det.urunAdi || null,
      sistemStokKolon: det.sistemStok || null,
      sayilanStokKolon: det.sayilanStok || null,
      stokKoduKolon: det.stokKodu || null,
      kategoriKolon: det.kategori || null,
      altKategoriKolon: det.altKategori || null,
      renkKolon: det.renk || null,
      bedenKolon: det.beden || null,
      markaKolon: det.marka || null,
      modelKolon: det.model || null,
      fiyatKolon: det.fiyat || null,
    };
    this.importReady.emit({ mapping, urunler: all });
  }

  private toRow(r: Record<string, unknown>, det: Detection): ExcelImportRow {
    const trimOrNull = (key: string) => {
      if (!key) return null;
      const v = String(r[key] ?? '').trim();
      return v === '' ? null : v;
    };
    const numOrNull = (key: string) => {
      if (!key) return null;
      const raw = r[key];
      if (raw === '' || raw === null || raw === undefined) return null;
      const n = this.toNumber(raw);
      return Number.isFinite(n) ? n : null;
    };
    return {
      barkod: String(r[det.barkod] ?? '').trim(),
      urunAdi: String(r[det.urunAdi] ?? '').trim(),
      sistemStok: this.toNumber(r[det.sistemStok]),
      sayilanStok: this.toNumber(r[det.sayilanStok]),
      stokKodu: trimOrNull(det.stokKodu),
      kategori: trimOrNull(det.kategori),
      altKategori: trimOrNull(det.altKategori),
      renk: trimOrNull(det.renk),
      beden: trimOrNull(det.beden),
      marka: trimOrNull(det.marka),
      model: trimOrNull(det.model),
      fiyat: numOrNull(det.fiyat),
    };
  }
}
