export type ImzaRolu = 'SayimBaskani' | 'MagazaYetkilisi';

export const IMZA_ROL_OPTIONS: ReadonlyArray<{ value: ImzaRolu; label: string }> = [
  { value: 'SayimBaskani', label: 'Sayım Başkanı' },
  { value: 'MagazaYetkilisi', label: 'Mağaza Yetkilisi' },
];

export interface BelgeTipi {
  id: string;
  firmaId: string;
  firmaAdi?: string | null;
  ad: string;
  aciklama?: string | null;
  gerekenImzaRolleri: ImzaRolu[];
  kaseGerekli: boolean;
  arsivlendi: boolean;
  olusturmaTarihi: string;
  guncellenmeTarihi?: string | null;
}

export interface BelgeTipiUpsert {
  firmaId?: string | null;
  ad: string;
  aciklama?: string | null;
  gerekenImzaRolleri: ImzaRolu[];
  kaseGerekli: boolean;
  arsivlendi: boolean;
}
