export interface OzelRaporDosya {
  id: string;
  ad: string;
  mimeType: string;
  boyut: number;
  yuklemeTarihi: string;
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
