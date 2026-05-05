import { FirmaTip } from '../admin/admin.models';

export type OturumDurum =
  | 'taslak'
  | 'excel_bekleniyor'
  | 'aktif'
  | 'kilitli'
  | 'tamamlandi'
  | 'iptal';

export const OTURUM_DURUM_LABELS: Record<OturumDurum, string> = {
  taslak: 'Taslak',
  excel_bekleniyor: 'Excel bekleniyor',
  aktif: 'Aktif',
  kilitli: 'Kilitli',
  tamamlandi: 'Tamamlandı',
  iptal: 'İptal',
};

export const OTURUM_DURUM_COLOR: Record<OturumDurum, string> = {
  taslak: 'bg-text-muted',
  excel_bekleniyor: 'bg-accent-warning',
  aktif: 'bg-accent-info',
  kilitli: 'bg-accent-warning',
  tamamlandi: 'bg-accent-success',
  iptal: 'bg-accent-danger',
};

export type UrunDurum =
  | 'beklemede'
  | 'tekrar_sayiliyor'
  | 'onaylandi'
  | 'iptal'
  | 'incele';

export const URUN_DURUM_LABELS: Record<UrunDurum, string> = {
  beklemede: 'Beklemede',
  tekrar_sayiliyor: 'Tekrar say',
  onaylandi: 'Onaylı',
  iptal: 'İptal',
  incele: 'İncele',
};

export interface OturumOzet {
  toplamUrun: number;
  beklemedeSayisi: number;
  tekrarSayilan: number;
  onaylanmis: number;
  iptalEdilmis: number;
  inceleme: number;
  toplamFarkPozitif: number;
  toplamFarkNegatif: number;
}

export interface Katilimci {
  kullaniciId: string;
  adSoyad: string;
  rol: string;
}

export interface ExcelMapping {
  barkodKolon?: string | null;
  urunAdiKolon?: string | null;
  sistemStokKolon?: string | null;
  sayilanStokKolon?: string | null;
  stokKoduKolon?: string | null;
  kategoriKolon?: string | null;
  altKategoriKolon?: string | null;
  renkKolon?: string | null;
  bedenKolon?: string | null;
  markaKolon?: string | null;
  modelKolon?: string | null;
  fiyatKolon?: string | null;
}

export type TalepDurum = 'beklemede' | 'onaylandi' | 'reddedildi';

export interface UrunDegisiklikTalebi {
  id: string;
  urunId: string;
  kullaniciId: string;
  kullaniciAdi: string;
  alan: string;
  eskiDeger?: string | null;
  yeniDeger?: string | null;
  gerekce?: string | null;
  durum: TalepDurum;
  kararVerenId?: string | null;
  kararVerenAdi?: string | null;
  kararSebep?: string | null;
  kararTarihi?: string | null;
  olusturmaTarihi: string;
}

export interface OturumUrun {
  id: string;
  barkod: string;
  urunAdi: string;
  sistemStok: number;
  sayilanStok: number;
  fark: number;
  durum: UrunDurum;
  atananSaymanId?: string | null;
  atananSaymanAdi?: string | null;
  yorumSayisi: number;
  kilitleyenKullaniciId?: string | null;
  kilitleyenAdi?: string | null;
  kilitlenmeTarihi?: string | null;

  stokKodu?: string | null;
  kategori?: string | null;
  altKategori?: string | null;
  renk?: string | null;
  beden?: string | null;
  marka?: string | null;
  model?: string | null;
  fiyat?: number | null;

  sistemFarki?: number | null;
  fiiliFarki?: number | null;
  fiyatFarki?: number | null;

  acikTalepler?: UrunDegisiklikTalebi[];
}

export interface OturumList {
  id: string;
  magazaId: string;
  magazaAdi: string;
  firmaId: string;
  firmaAdi: string;
  firmaTip: FirmaTip;
  atamaId?: string | null;
  tarih: string;
  durum: OturumDurum;
  ozetler: OturumOzet;
  katilimcilar: Katilimci[];
  olusturmaTarihi: string;
}

export interface OturumDetail extends OturumList {
  urunler: OturumUrun[];
  excelMapping: ExcelMapping;
}

export interface OturumCreate {
  magazaId: string;
  tarih: string; // yyyy-MM-dd
  atamaId?: string;
  katilimcilar: { kullaniciId: string; rol: string }[];
}

export interface ExcelImportRow {
  barkod: string;
  urunAdi: string;
  sistemStok: number;
  sayilanStok: number;
  stokKodu?: string | null;
  kategori?: string | null;
  altKategori?: string | null;
  renk?: string | null;
  beden?: string | null;
  marka?: string | null;
  model?: string | null;
  fiyat?: number | null;
}

export interface ExcelImportPayload {
  mapping: ExcelMapping;
  urunler: ExcelImportRow[];
}

// ─── Firma tipine göre Excel kolon profilleri ───────────────────────────────
// "Sabit" kolonlar (her firmada algılanır + UI'da sabit gösterilir):
//   barkod, sistemStok, sayilanStok, fiyat
// "Tip bazlı" kolonlar — aşağıdaki profile bakılır.

export type ProfilKolonAnahtari =
  | 'stokKodu'
  | 'kategori'
  | 'altKategori'
  | 'renk'
  | 'beden'
  | 'marka'
  | 'model';

export interface ProfilKolonu {
  key: ProfilKolonAnahtari;
  label: string;
  keywords: string[];
}

const KOLON_TANIMLARI: Record<ProfilKolonAnahtari, ProfilKolonu> = {
  stokKodu: {
    key: 'stokKodu',
    label: 'Stok Kodu',
    keywords: ['stok kodu', 'stokkodu', 'model kodu', 'sku', 'ürün kodu', 'urun kodu', 'plu', 'item code'],
  },
  kategori: {
    key: 'kategori',
    label: 'Kategori',
    keywords: ['kategori', 'category', 'reyon', 'bölüm', 'bolum', 'departman'],
  },
  altKategori: {
    key: 'altKategori',
    label: 'Alt Kategori',
    keywords: ['alt kategori', 'altkategori', 'subcategory', 'alt grup', 'alt-kategori', 'tür', 'tur'],
  },
  renk: {
    key: 'renk',
    label: 'Renk',
    keywords: ['renk', 'color', 'colour'],
  },
  beden: {
    key: 'beden',
    label: 'Beden',
    keywords: ['beden', 'size', 'numara', 'no'],
  },
  marka: {
    key: 'marka',
    label: 'Marka',
    keywords: ['marka', 'brand'],
  },
  model: {
    key: 'model',
    label: 'Model',
    keywords: ['model'],
  },
};

export const FIRMA_TIP_PROFILI: Record<FirmaTip, ProfilKolonu[]> = {
  tekstil: ['stokKodu', 'kategori', 'altKategori', 'renk', 'beden'].map((k) => KOLON_TANIMLARI[k as ProfilKolonAnahtari]),
  market: ['stokKodu', 'kategori', 'altKategori'].map((k) => KOLON_TANIMLARI[k as ProfilKolonAnahtari]),
  istasyon_market: ['stokKodu', 'kategori'].map((k) => KOLON_TANIMLARI[k as ProfilKolonAnahtari]),
  kozmetik: ['stokKodu', 'kategori', 'altKategori', 'marka'].map((k) => KOLON_TANIMLARI[k as ProfilKolonAnahtari]),
  elektronik: ['stokKodu', 'kategori', 'altKategori', 'marka', 'model'].map((k) => KOLON_TANIMLARI[k as ProfilKolonAnahtari]),
  mobilya: ['stokKodu', 'kategori', 'altKategori', 'renk'].map((k) => KOLON_TANIMLARI[k as ProfilKolonAnahtari]),
  yapi_market: ['stokKodu', 'kategori', 'altKategori'].map((k) => KOLON_TANIMLARI[k as ProfilKolonAnahtari]),
  eczane: ['stokKodu', 'kategori', 'altKategori'].map((k) => KOLON_TANIMLARI[k as ProfilKolonAnahtari]),
  ayakkabi: ['stokKodu', 'kategori', 'renk', 'beden'].map((k) => KOLON_TANIMLARI[k as ProfilKolonAnahtari]),
  otomotiv: ['stokKodu', 'kategori', 'marka', 'model'].map((k) => KOLON_TANIMLARI[k as ProfilKolonAnahtari]),
  kirtasiye: ['stokKodu', 'kategori', 'altKategori'].map((k) => KOLON_TANIMLARI[k as ProfilKolonAnahtari]),
  diger: ['stokKodu', 'kategori'].map((k) => KOLON_TANIMLARI[k as ProfilKolonAnahtari]),
};
