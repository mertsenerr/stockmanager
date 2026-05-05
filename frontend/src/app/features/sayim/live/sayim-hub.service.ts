import { Injectable, inject } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../core/auth/auth.service';

export interface KullaniciKatildiEvent {
  oturumId: string;
  kullaniciId: string;
  kullaniciAdi: string;
  rol: string;
}
export interface KullaniciAyrildiEvent {
  oturumId: string;
  kullaniciId: string;
}
export interface HucreKilitlendiEvent {
  oturumId: string;
  urunId: string;
  alan: string;
  kullaniciId: string;
  kullaniciAdi: string;
  expiresAt: string;
}
export interface HucreSerbestEvent {
  oturumId: string;
  urunId: string;
  alan: string;
}
export interface UrunGuncellendiEvent {
  oturumId: string;
  patch: {
    urunId: string;
    sayilanStok?: number | null;
    fark: number;
    durum?: string | null;
    atananSaymanId?: string | null;
    yorumSayisi: number;
    kullaniciId: string;
    kullaniciAdi: string;
    tarih: string;
  };
  ozet: {
    toplamUrun: number;
    beklemede: number;
    tekrarSayilan: number;
    onaylanmis: number;
    iptalEdilmis: number;
    inceleme: number;
    toplamFarkPozitif: number;
    toplamFarkNegatif: number;
  };
}
export interface YorumEklendiEvent {
  oturumId: string;
  urunId: string;
  kullaniciId: string;
  kullaniciAdi: string;
  mesaj: string;
  tarih: string;
}

export interface TalepOlusturulduEvent {
  oturumId: string;
  talep: import('../sayim.models').UrunDegisiklikTalebi;
}
export interface TalepOnaylandiEvent {
  oturumId: string;
  urunId: string;
  talepId: string;
  kararVerenAdi: string;
  kararTarihi: string;
}
export interface TalepReddedildiEvent {
  oturumId: string;
  urunId: string;
  talepId: string;
  kararVerenAdi: string;
  sebep?: string | null;
  kararTarihi: string;
}

export interface CallParticipantDto {
  connectionId: string;
  kullaniciId: string;
  kullaniciAdi: string;
  rol: string;
}
export interface CallRosterEvent {
  oturumId: string;
  participants: CallParticipantDto[];
}
export interface CallParticipantJoinedEvent {
  oturumId: string;
  kullaniciId: string;
  kullaniciAdi: string;
  connectionId: string;
}
export interface CallParticipantLeftEvent {
  oturumId: string;
  connectionId: string;
}
export interface CallSignalEvent {
  oturumId: string;
  fromConnectionId: string;
  fromKullaniciId: string;
  fromKullaniciAdi: string;
  type: 'offer' | 'answer' | 'ice';
  payload: string;
}
export interface CallRingingEvent {
  oturumId: string;
  baslatanKullaniciId: string;
  baslatanKullaniciAdi: string;
}

@Injectable({ providedIn: 'root' })
export class SayimHubService {
  private readonly auth = inject(AuthService);
  private connection?: HubConnection;
  private joinedOturumId: string | null = null;

  readonly katildi$ = new Subject<KullaniciKatildiEvent>();
  readonly ayrildi$ = new Subject<KullaniciAyrildiEvent>();
  readonly hucreKilitlendi$ = new Subject<HucreKilitlendiEvent>();
  readonly hucreSerbest$ = new Subject<HucreSerbestEvent>();
  readonly urunGuncellendi$ = new Subject<UrunGuncellendiEvent>();
  readonly yorumEklendi$ = new Subject<YorumEklendiEvent>();
  readonly talepOlusturuldu$ = new Subject<TalepOlusturulduEvent>();
  readonly talepOnaylandi$ = new Subject<TalepOnaylandiEvent>();
  readonly talepReddedildi$ = new Subject<TalepReddedildiEvent>();
  readonly callRoster$ = new Subject<CallRosterEvent>();
  readonly callParticipantJoined$ = new Subject<CallParticipantJoinedEvent>();
  readonly callParticipantLeft$ = new Subject<CallParticipantLeftEvent>();
  readonly callSignal$ = new Subject<CallSignalEvent>();
  readonly callRinging$ = new Subject<CallRingingEvent>();
  readonly disconnected$ = new Subject<void>();

  /** Bağlı kullanıcıyı global hub'a bağlar — gelen arama bildirimi vs. için her sayfa açıkken aktif olmalı. */
  async connect(): Promise<void> {
    await this.ensureConnection();
  }

  async joinOturum(oturumId: string): Promise<void> {
    await this.ensureConnection();
    if (!this.connection) return;
    if (this.joinedOturumId && this.joinedOturumId !== oturumId) {
      await this.connection.invoke('OturumdanAyril', this.joinedOturumId).catch(() => undefined);
    }
    await this.connection.invoke('OturumaKatil', oturumId);
    this.joinedOturumId = oturumId;
  }

  async leaveOturum(): Promise<void> {
    if (!this.connection || !this.joinedOturumId) return;
    await this.connection.invoke('OturumdanAyril', this.joinedOturumId).catch(() => undefined);
    this.joinedOturumId = null;
  }

  async lockCell(oturumId: string, urunId: string, alan: string): Promise<void> {
    if (!this.connection) return;
    await this.connection.invoke('HucreKilitle', oturumId, urunId, alan).catch(() => undefined);
  }

  async releaseCell(oturumId: string, urunId: string, alan: string): Promise<void> {
    if (!this.connection) return;
    await this.connection.invoke('HucreSerbestBirak', oturumId, urunId, alan).catch(() => undefined);
  }

  async updateUrun(
    oturumId: string,
    urunId: string,
    payload: { sayilanStok?: number; durum?: string; atananSaymanId?: string; yorum?: string },
  ): Promise<void> {
    if (!this.connection) return;
    await this.connection.invoke(
      'UrunGuncelle',
      oturumId, urunId,
      payload.sayilanStok ?? null,
      payload.durum ?? null,
      payload.atananSaymanId ?? null,
      payload.yorum ?? null,
    );
  }

  async talepOlustur(
    oturumId: string, urunId: string, alan: string, yeniDeger: number, gerekce?: string,
  ): Promise<void> {
    if (!this.connection) return;
    await this.connection.invoke('TalepOlustur', oturumId, urunId, alan, yeniDeger, gerekce ?? null);
  }

  async talepOnayla(oturumId: string, urunId: string, talepId: string): Promise<void> {
    if (!this.connection) return;
    await this.connection.invoke('TalepOnayla', oturumId, urunId, talepId);
  }

  async talepReddet(oturumId: string, urunId: string, talepId: string, sebep?: string): Promise<void> {
    if (!this.connection) return;
    await this.connection.invoke('TalepReddet', oturumId, urunId, talepId, sebep ?? null);
  }

  async callJoin(oturumId: string): Promise<void> {
    await this.ensureConnection();
    if (!this.connection) return;
    await this.connection.invoke('CallJoin', oturumId);
  }

  async callLeave(oturumId: string): Promise<void> {
    if (!this.connection) return;
    await this.connection.invoke('CallLeave', oturumId).catch(() => undefined);
  }

  async callSignal(
    oturumId: string,
    toConnectionId: string,
    type: 'offer' | 'answer' | 'ice',
    payload: string,
  ): Promise<void> {
    if (!this.connection) return;
    await this.connection.invoke('CallSignal', oturumId, toConnectionId, type, payload);
  }

  async callInvite(oturumId: string, hedefKullaniciId: string): Promise<void> {
    await this.ensureConnection();
    if (!this.connection) return;
    await this.connection.invoke('CallInvite', oturumId, hedefKullaniciId);
  }

  async disconnect(): Promise<void> {
    if (!this.connection) return;
    try {
      await this.leaveOturum();
      await this.connection.stop();
    } finally {
      this.connection = undefined;
      this.joinedOturumId = null;
    }
  }

  private async ensureConnection(): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) return;
    this.connection = new HubConnectionBuilder()
      .withUrl(`${environment.hubBaseUrl}/hubs/sayim`, {
        accessTokenFactory: () => this.auth.accessToken() ?? '',
        withCredentials: true,
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('KullaniciKatildi', (oturumId, kullaniciId, kullaniciAdi, rol) =>
      this.katildi$.next({ oturumId, kullaniciId, kullaniciAdi, rol }));
    this.connection.on('KullaniciAyrildi', (oturumId, kullaniciId) =>
      this.ayrildi$.next({ oturumId, kullaniciId }));
    this.connection.on('HucreKilitlendi', (oturumId, urunId, alan, kullaniciId, kullaniciAdi, expiresAt) =>
      this.hucreKilitlendi$.next({ oturumId, urunId, alan, kullaniciId, kullaniciAdi, expiresAt }));
    this.connection.on('HucreSerbestBirakildi', (oturumId, urunId, alan) =>
      this.hucreSerbest$.next({ oturumId, urunId, alan }));
    this.connection.on('UrunGuncellendi', (oturumId, patch, ozet) =>
      this.urunGuncellendi$.next({ oturumId, patch, ozet }));
    this.connection.on('YorumEklendi', (oturumId, urunId, kullaniciId, kullaniciAdi, mesaj, tarih) =>
      this.yorumEklendi$.next({ oturumId, urunId, kullaniciId, kullaniciAdi, mesaj, tarih }));
    this.connection.on('TalepOlusturuldu', (oturumId, talep) =>
      this.talepOlusturuldu$.next({ oturumId, talep }));
    this.connection.on('TalepOnaylandi', (oturumId, urunId, talepId, kararVerenAdi, kararTarihi) =>
      this.talepOnaylandi$.next({ oturumId, urunId, talepId, kararVerenAdi, kararTarihi }));
    this.connection.on('TalepReddedildi', (oturumId, urunId, talepId, kararVerenAdi, sebep, kararTarihi) =>
      this.talepReddedildi$.next({ oturumId, urunId, talepId, kararVerenAdi, sebep, kararTarihi }));

    this.connection.on('CallRoster', (oturumId, participants) =>
      this.callRoster$.next({ oturumId, participants: participants ?? [] }));
    this.connection.on('CallParticipantJoined', (oturumId, kullaniciId, kullaniciAdi, connectionId) =>
      this.callParticipantJoined$.next({ oturumId, kullaniciId, kullaniciAdi, connectionId }));
    this.connection.on('CallParticipantLeft', (oturumId, connectionId) =>
      this.callParticipantLeft$.next({ oturumId, connectionId }));
    this.connection.on('CallSignal', (oturumId, fromConnectionId, fromKullaniciId, fromKullaniciAdi, type, payload) =>
      this.callSignal$.next({ oturumId, fromConnectionId, fromKullaniciId, fromKullaniciAdi, type, payload }));
    this.connection.on('CallRinging', (oturumId, baslatanKullaniciId, baslatanKullaniciAdi) =>
      this.callRinging$.next({ oturumId, baslatanKullaniciId, baslatanKullaniciAdi }));

    this.connection.onclose(() => this.disconnected$.next());

    await this.connection.start();
  }
}
