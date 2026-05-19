export interface DosyaImza {
  id: string;
  rol: string;
  kullaniciId: string;
  kullaniciAdSoyad: string;
  imzalanmaTarihi: string;
}

export interface KaseDamga {
  basanKullaniciId: string;
  basanAdSoyad: string;
  tarih: string;
}

export interface ImzaSlotSnapshot {
  rol: string;
  konum: string;
}

export interface OzelRaporDosya {
  id: string;
  ad: string;
  mimeType: string;
  boyut: number;
  yuklemeTarihi: string;
  belgeTipiId?: string | null;
  belgeTipiAdi?: string | null;
  imzaSlotlari: ImzaSlotSnapshot[];
  kaseGerekli: boolean;
  kaseKonum?: string | null;
  imzalar: DosyaImza[];
  kase?: KaseDamga | null;
}

export interface OzelRapor {
  id: string;
  ad: string;
  aciklama?: string | null;
  olusturanKullaniciId: string;
  olusturanAdSoyad?: string | null;
  erisebilenKullaniciIds: string[];
  dosyalar: OzelRaporDosya[];
  olusturmaTarihi: string;
  guncellenmeTarihi?: string | null;
  duzenleyebilir: boolean;
}

export interface OzelRaporUpsert {
  ad: string;
  aciklama?: string | null;
  erisebilenKullaniciIds: string[];
}
