export interface MagazaSapma {
  magazaId: string;
  magazaAdi: string;
  firmaAdi: string;
  oturumSayisi: number;
  toplamUrun: number;
  toplamFarkliUrun: number;
  toplamFarkPozitif: number;
  toplamFarkNegatif: number;
  sapmaYuzdesi: number;
}

export interface SaymanPerformans {
  kullaniciId: string;
  adSoyad: string;
  oturumSayisi: number;
  toplamGuncelleme: number;
  toplamYorum: number;
  sonAktivite?: string | null;
}

export interface AuditLogItem {
  id: string;
  tarih: string;
  kullaniciId?: string | null;
  kullaniciAdi: string;
  kullaniciRol: string;
  aksiyon: string;
  hedef?: string | null;
  hedefId?: string | null;
  eskiDeger?: string | null;
  yeniDeger?: string | null;
  ipAdres?: string | null;
  basarili: boolean;
}

export interface AuditPage {
  items: AuditLogItem[];
  total: number;
  skip: number;
  take: number;
}

export const AKSIYON_LABELS: Record<string, string> = {
  'auth.login.ok': 'Giriş başarılı',
  'auth.login.fail': 'Giriş başarısız',
  'auth.logout': 'Çıkış',
  'auth.password.reset': 'Parola sıfırlama',
  'oturum.create': 'Oturum oluştu',
  'oturum.update': 'Oturum güncellendi',
  'oturum.durum-change': 'Durum değişti',
  'oturum.excel-import': 'Excel yüklendi',
  'oturum.delete': 'Oturum iptal',
  'oturum.urun-update': 'Ürün güncellendi',
  'firma.create': 'Firma oluştu',
  'firma.update': 'Firma güncellendi',
  'firma.delete': 'Firma silindi',
  'magaza.create': 'Mağaza oluştu',
  'magaza.update': 'Mağaza güncellendi',
  'magaza.delete': 'Mağaza silindi',
  'kullanici.create': 'Kullanıcı oluştu',
  'kullanici.update': 'Kullanıcı güncellendi',
  'kullanici.delete': 'Kullanıcı silindi',
  'atama.create': 'Atama oluştu',
  'atama.move-date': 'Atama taşındı',
  'atama.delete': 'Atama silindi',
};
