export type ImzaRolu = 'SayimBaskani' | 'MagazaYetkilisi';

export const IMZA_ROL_OPTIONS: ReadonlyArray<{ value: ImzaRolu; label: string }> = [
  { value: 'SayimBaskani', label: 'Sayım Başkanı' },
  { value: 'MagazaYetkilisi', label: 'Mağaza Yetkilisi' },
];

export type ImzaKonum = 'SolAlt' | 'OrtaAlt' | 'SagAlt';

export const IMZA_KONUM_OPTIONS: ReadonlyArray<{ value: ImzaKonum; label: string }> = [
  { value: 'SolAlt', label: 'Sol alt' },
  { value: 'OrtaAlt', label: 'Orta alt' },
  { value: 'SagAlt', label: 'Sağ alt' },
];

export interface ImzaSlot {
  rol: ImzaRolu;
  konum: ImzaKonum;
}

export interface BelgeTipi {
  id: string;
  firmaId: string;
  firmaAdi?: string | null;
  ad: string;
  aciklama?: string | null;
  imzaSlotlari: ImzaSlot[];
  kaseGerekli: boolean;
  kaseKonum?: ImzaKonum | null;
  arsivlendi: boolean;
  olusturmaTarihi: string;
  guncellenmeTarihi?: string | null;
}

export interface BelgeTipiUpsert {
  firmaId?: string | null;
  ad: string;
  aciklama?: string | null;
  imzaSlotlari: ImzaSlot[];
  kaseGerekli: boolean;
  kaseKonum?: ImzaKonum | null;
  arsivlendi: boolean;
}
