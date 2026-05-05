export type AtamaDurum = 'planlandi' | 'tamamlandi' | 'iptal';

export const ATAMA_DURUM_LABELS: Record<AtamaDurum, string> = {
  planlandi: 'Planlandı',
  tamamlandi: 'Tamamlandı',
  iptal: 'İptal',
};

export interface Atama {
  id: string;
  magazaId: string;
  magazaAdi: string;
  firmaId: string;
  firmaAdi: string;
  tarih: string; // ISO datetime (UTC midnight)
  baslangicSaati?: string | null;
  bitisSaati?: string | null;
  yoneticiKullaniciId: string;
  yoneticiAdi: string;
  saymanKullaniciIds: string[];
  saymanAdlari: string[];
  notlar?: string | null;
  durum: AtamaDurum;
}

export interface AtamaUpsert {
  magazaId: string;
  tarih: string; // yyyy-MM-dd
  baslangicSaati?: string | null;
  bitisSaati?: string | null;
  yoneticiKullaniciId: string;
  saymanKullaniciIds: string[];
  notlar?: string | null;
  durum: AtamaDurum;
}

const FIRMA_PALETTE = [
  '#3b82f6', // info blue
  '#10b981', // success green
  '#f59e0b', // warning amber
  '#ef4444', // danger red
  '#a855f7', // purple
  '#ec4899', // pink
  '#14b8a6', // teal
  '#f97316', // orange
];

export function colorForFirma(firmaId: string): string {
  // Stable hash so the same firma keeps the same color across sessions.
  let hash = 0;
  for (let i = 0; i < firmaId.length; i++) {
    hash = (hash * 31 + firmaId.charCodeAt(i)) | 0;
  }
  const idx = Math.abs(hash) % FIRMA_PALETTE.length;
  return FIRMA_PALETTE[idx];
}
