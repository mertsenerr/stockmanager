import { UserRole } from '../../core/auth/auth.models';

export type FirmaTip =
  | 'tekstil'
  | 'market'
  | 'istasyon_market'
  | 'kozmetik'
  | 'elektronik'
  | 'mobilya'
  | 'yapi_market'
  | 'eczane'
  | 'ayakkabi'
  | 'otomotiv'
  | 'kirtasiye'
  | 'diger';

export const FIRMA_TIP_LABELS: Record<FirmaTip, string> = {
  tekstil: 'Tekstil & Hazır Giyim',
  market: 'Market & Gıda',
  istasyon_market: 'İstasyon Marketi',
  kozmetik: 'Kozmetik & Kişisel Bakım',
  elektronik: 'Elektronik & Beyaz Eşya',
  mobilya: 'Mobilya & Ev Dekorasyon',
  yapi_market: 'Yapı Market & Hırdavat',
  eczane: 'Eczane',
  ayakkabi: 'Ayakkabı & Çanta',
  otomotiv: 'Otomotiv & Yedek Parça',
  kirtasiye: 'Kırtasiye & Kitap',
  diger: 'Diğer',
};

export const FIRMA_TIP_OPTIONS: { value: FirmaTip; label: string }[] = (
  Object.keys(FIRMA_TIP_LABELS) as FirmaTip[]
).map((value) => ({ value, label: FIRMA_TIP_LABELS[value] }));

export interface Firma {
  id: string;
  ad: string;
  kisaltma?: string;
  tip: FirmaTip;
  logoUrl?: string | null;
  aktifMi: boolean;
  olusturmaTarihi: string;
}

export interface FirmaUpsert {
  ad: string;
  kisaltma?: string;
  tip: FirmaTip;
  logoUrl?: string | null;
  aktifMi: boolean;
}

export interface Koordinat {
  lat: number;
  lng: number;
}

export interface Magaza {
  id: string;
  firmaId: string;
  firmaAdi?: string | null;
  ad: string;
  sehir: string;
  ilce: string;
  adres: string;
  koordinat?: Koordinat | null;
  muduruKullaniciId?: string | null;
  muduruAdSoyad?: string | null;
  aktifMi: boolean;
}

export interface MagazaUpsert {
  firmaId: string;
  ad: string;
  sehir: string;
  ilce: string;
  adres: string;
  koordinat?: Koordinat | null;
  muduruKullaniciId?: string | null;
  aktifMi: boolean;
}

export interface KullaniciList {
  id: string;
  email: string;
  adSoyad: string;
  rol: UserRole;
  firmaId?: string | null;
  firmaIds: string[];
  magazaIds: string[];
  aktifMi: boolean;
  sonGirisTarihi?: string | null;
  olusturmaTarihi: string;
}

export interface KullaniciCreate {
  email: string;
  adSoyad: string;
  rol: UserRole;
  password: string;
  firmaIds: string[];
  magazaIds: string[];
  aktifMi: boolean;
}

export interface KullaniciUpdate {
  adSoyad: string;
  rol: UserRole;
  firmaIds: string[];
  magazaIds: string[];
  aktifMi: boolean;
  newPassword?: string;
}
