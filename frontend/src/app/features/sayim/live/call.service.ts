import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Subject, takeUntil } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { SayimHubService } from './sayim-hub.service';

export interface RemotePeer {
  connectionId: string;
  kullaniciId: string;
  kullaniciAdi: string;
  stream: MediaStream;
  connectionState: RTCPeerConnectionState;
}

interface PeerEntry {
  pc: RTCPeerConnection;
  kullaniciId: string;
  kullaniciAdi: string;
  stream: MediaStream;
  /** Glare çözümü — peer'in connectionId'si bizimkinden büyükse polite olur. */
  polite: boolean;
  makingOffer: boolean;
  ignoreOffer: boolean;
}

/**
 * Mesh tabanlı WebRTC orchestration. Her peer için bir RTCPeerConnection.
 * Sinyalleşme mevcut SayimHub üzerinden — ekstra altyapı yok.
 *
 * "Perfect negotiation" deseni: aynı anda iki peer offer atarsa (glare),
 * connectionId karşılaştırmasıyla bir taraf "impolite" olup karşıdan gelen
 * offer'ı yok sayar; diğer taraf "polite" olup kendi offer'ını rollback eder.
 */
@Injectable({ providedIn: 'root' })
export class CallService {
  private readonly hub = inject(SayimHubService);
  private readonly http = inject(HttpClient);

  private oturumId: string | null = null;
  private myConnectionId: string | null = null;
  private localStream: MediaStream | null = null;
  private iceServers: RTCIceServer[] = [{ urls: ['stun:stun.l.google.com:19302'] }];
  private readonly destroy$ = new Subject<void>();
  private readonly peers = new Map<string, PeerEntry>();

  readonly mode = signal<'off' | 'audio' | 'video'>('off');
  readonly micMuted = signal(false);
  readonly camOff = signal(false);
  readonly screenSharing = signal(false);
  readonly errorMsg = signal<string>('');
  readonly localStreamSig = signal<MediaStream | null>(null);
  readonly remotes = signal<RemotePeer[]>([]);

  readonly active = computed(() => this.mode() !== 'off');

  /** Kameralar ve mikrofonlar (cihaz seçimi için). */
  readonly devices = signal<{ cameras: MediaDeviceInfo[]; mics: MediaDeviceInfo[] }>({
    cameras: [],
    mics: [],
  });
  readonly selectedCameraId = signal<string | null>(null);
  readonly selectedMicId = signal<string | null>(null);

  async start(oturumId: string, mode: 'audio' | 'video'): Promise<void> {
    if (this.mode() !== 'off') return;
    this.errorMsg.set('');
    this.oturumId = oturumId;

    try {
      await this.fetchIceServers();
      await this.acquireLocalStream(mode);
      this.subscribeHub();
      await this.hub.callJoin(oturumId);
      this.mode.set(mode);
      // Cihaz listesini doldur — getUserMedia sonrası label'lar dolmuş olur.
      this.refreshDevices();
    } catch (err) {
      console.error('[call] start failed', err);
      this.errorMsg.set(this.humanizeError(err));
      await this.stop();
    }
  }

  async stop(): Promise<void> {
    const oid = this.oturumId;
    this.destroy$.next();
    for (const entry of this.peers.values()) {
      try { entry.pc.close(); } catch { /* noop */ }
    }
    this.peers.clear();
    this.publishRemotes();
    if (this.localStream) {
      for (const t of this.localStream.getTracks()) t.stop();
      this.localStream = null;
    }
    this.localStreamSig.set(null);
    if (oid) await this.hub.callLeave(oid).catch(() => undefined);
    this.oturumId = null;
    this.myConnectionId = null;
    this.mode.set('off');
    this.micMuted.set(false);
    this.camOff.set(false);
    this.screenSharing.set(false);
  }

  toggleMic(): void {
    if (!this.localStream) return;
    const next = !this.micMuted();
    for (const t of this.localStream.getAudioTracks()) t.enabled = !next;
    this.micMuted.set(next);
  }

  toggleCam(): void {
    if (!this.localStream || this.mode() !== 'video') return;
    const next = !this.camOff();
    for (const t of this.localStream.getVideoTracks()) t.enabled = !next;
    this.camOff.set(next);
  }

  async startScreenShare(): Promise<void> {
    if (this.mode() !== 'video' || !this.localStream) return;
    try {
      const screen = await navigator.mediaDevices.getDisplayMedia({ video: true, audio: false });
      const screenTrack = screen.getVideoTracks()[0];
      if (!screenTrack) return;
      // Mevcut kamera track'ini kapat, screen track'ini ekle.
      const camTrack = this.localStream.getVideoTracks()[0];
      this.localStream.removeTrack(camTrack);
      camTrack?.stop();
      this.localStream.addTrack(screenTrack);
      this.localStreamSig.set(new MediaStream(this.localStream.getTracks()));
      // Tüm peer'lerde sender'ları replaceTrack ile güncelle.
      for (const entry of this.peers.values()) {
        const sender = entry.pc.getSenders().find((s) => s.track?.kind === 'video');
        await sender?.replaceTrack(screenTrack);
      }
      this.screenSharing.set(true);
      // Kullanıcı tarayıcı UI'sından durdurursa kameraya geri dön.
      screenTrack.addEventListener('ended', () => { this.stopScreenShare().catch(() => undefined); });
    } catch (err) {
      console.warn('[call] screen share denied', err);
    }
  }

  async stopScreenShare(): Promise<void> {
    if (!this.screenSharing() || !this.localStream) return;
    const screenTrack = this.localStream.getVideoTracks()[0];
    this.localStream.removeTrack(screenTrack);
    screenTrack?.stop();
    // Yeniden kamera al.
    try {
      const cam = await navigator.mediaDevices.getUserMedia({
        video: this.selectedCameraId()
          ? { deviceId: { exact: this.selectedCameraId()! } }
          : true,
        audio: false,
      });
      const camTrack = cam.getVideoTracks()[0];
      this.localStream.addTrack(camTrack);
      this.localStreamSig.set(new MediaStream(this.localStream.getTracks()));
      for (const entry of this.peers.values()) {
        const sender = entry.pc.getSenders().find((s) => s.track?.kind === 'video');
        await sender?.replaceTrack(camTrack);
      }
    } catch (err) {
      console.error('[call] re-acquire camera failed', err);
    }
    this.screenSharing.set(false);
  }

  async setCamera(deviceId: string): Promise<void> {
    if (!this.localStream || this.mode() !== 'video') {
      this.selectedCameraId.set(deviceId);
      return;
    }
    try {
      const newStream = await navigator.mediaDevices.getUserMedia({
        video: { deviceId: { exact: deviceId } }, audio: false,
      });
      const newTrack = newStream.getVideoTracks()[0];
      const oldTrack = this.localStream.getVideoTracks()[0];
      this.localStream.removeTrack(oldTrack);
      oldTrack?.stop();
      this.localStream.addTrack(newTrack);
      this.localStreamSig.set(new MediaStream(this.localStream.getTracks()));
      for (const entry of this.peers.values()) {
        const sender = entry.pc.getSenders().find((s) => s.track?.kind === 'video');
        await sender?.replaceTrack(newTrack);
      }
      this.selectedCameraId.set(deviceId);
    } catch (err) {
      console.error('[call] setCamera failed', err);
    }
  }

  async setMic(deviceId: string): Promise<void> {
    if (!this.localStream) {
      this.selectedMicId.set(deviceId);
      return;
    }
    try {
      const newStream = await navigator.mediaDevices.getUserMedia({
        audio: { deviceId: { exact: deviceId }, echoCancellation: true, noiseSuppression: true, autoGainControl: true },
        video: false,
      });
      const newTrack = newStream.getAudioTracks()[0];
      const oldTrack = this.localStream.getAudioTracks()[0];
      this.localStream.removeTrack(oldTrack);
      oldTrack?.stop();
      this.localStream.addTrack(newTrack);
      this.localStreamSig.set(new MediaStream(this.localStream.getTracks()));
      for (const entry of this.peers.values()) {
        const sender = entry.pc.getSenders().find((s) => s.track?.kind === 'audio');
        await sender?.replaceTrack(newTrack);
      }
      this.selectedMicId.set(deviceId);
    } catch (err) {
      console.error('[call] setMic failed', err);
    }
  }

  // ─── Internals ─────────────────────────────────────────────────────────────

  private async fetchIceServers(): Promise<void> {
    try {
      const res = await this.http.get<{ iceServers: RTCIceServer[] }>(
        `${environment.apiBaseUrl}/api/call/ice-servers`,
      ).toPromise();
      if (res?.iceServers?.length) this.iceServers = res.iceServers;
    } catch {
      // STUN'a düş, sessiz geç.
    }
  }

  private async acquireLocalStream(mode: 'audio' | 'video'): Promise<void> {
    const constraints: MediaStreamConstraints = {
      audio: {
        deviceId: this.selectedMicId() ? { exact: this.selectedMicId()! } : undefined,
        echoCancellation: true,
        noiseSuppression: true,
        autoGainControl: true,
      },
      video: mode === 'video'
        ? {
            deviceId: this.selectedCameraId() ? { exact: this.selectedCameraId()! } : undefined,
            width: { ideal: 1280 },
            height: { ideal: 720 },
            facingMode: 'user',
          }
        : false,
    };
    this.localStream = await navigator.mediaDevices.getUserMedia(constraints);
    this.localStreamSig.set(this.localStream);
  }

  private async refreshDevices(): Promise<void> {
    try {
      const all = await navigator.mediaDevices.enumerateDevices();
      this.devices.set({
        cameras: all.filter((d) => d.kind === 'videoinput'),
        mics: all.filter((d) => d.kind === 'audioinput'),
      });
    } catch {
      // ignore
    }
  }

  private subscribeHub(): void {
    this.hub.callRoster$.pipe(takeUntil(this.destroy$)).subscribe(async (e) => {
      if (e.oturumId !== this.oturumId) return;
      // Roster bize yalnızca ilk join'de gelir; mevcut peer'lerin her biri için offer hazırla.
      // myConnectionId'mizi öğrenmek için: roster'da kendimiz yokuz, ama her peer'in connectionId'si bizimkiyle kıyaslanacak.
      // SignalR connectionId'mizi öğrenmek zor; bunun yerine "polite" rolünü roster'da gözükmemizden çıkar:
      // İlk gelen biziz → tüm karşı peer'ler bizden ÖNCE bağlandı → onlar offer atacak (polite=true bizim için, biz cevap vereceğiz).
      // Burada glare hemen hemen imkânsız çünkü roster mantığı tek yönlü. Yine de perfect-negotiation'ı uyguluyoruz.
      for (const p of e.participants) {
        await this.ensurePeer(p.connectionId, p.kullaniciId, p.kullaniciAdi, /*initiate*/ true);
      }
    });

    this.hub.callParticipantJoined$.pipe(takeUntil(this.destroy$)).subscribe(async (e) => {
      if (e.oturumId !== this.oturumId) return;
      // Yeni katılan biziz değiliz; karşı taraf offer atacak. Biz pasif bekleriz ama PC'yi şimdiden hazır tutarız.
      await this.ensurePeer(e.connectionId, e.kullaniciId, e.kullaniciAdi, /*initiate*/ false);
    });

    this.hub.callParticipantLeft$.pipe(takeUntil(this.destroy$)).subscribe((e) => {
      if (e.oturumId !== this.oturumId) return;
      const entry = this.peers.get(e.connectionId);
      if (!entry) return;
      try { entry.pc.close(); } catch { /* noop */ }
      this.peers.delete(e.connectionId);
      this.publishRemotes();
    });

    this.hub.callSignal$.pipe(takeUntil(this.destroy$)).subscribe(async (e) => {
      if (e.oturumId !== this.oturumId) return;
      await this.handleSignal(e.fromConnectionId, e.fromKullaniciId, e.fromKullaniciAdi, e.type, e.payload);
    });
  }

  private async ensurePeer(
    connectionId: string,
    kullaniciId: string,
    kullaniciAdi: string,
    initiate: boolean,
  ): Promise<PeerEntry> {
    const existing = this.peers.get(connectionId);
    if (existing) return existing;

    const pc = new RTCPeerConnection({ iceServers: this.iceServers });
    const remoteStream = new MediaStream();
    // Polite rolü: connectionId string karşılaştırmasıyla deterministik belirle.
    // Bizimki bilinmiyor — roster'da kendimiz yok — bu yüzden simetrik kuralı şöyle uygula:
    // initiate=true ise biz impolite; karşı taraf bize cevap verir.
    // initiate=false ise biz polite; karşı tarafın offer'ını her durumda kabul ederiz.
    const polite = !initiate;

    const entry: PeerEntry = {
      pc,
      kullaniciId,
      kullaniciAdi,
      stream: remoteStream,
      polite,
      makingOffer: false,
      ignoreOffer: false,
    };
    this.peers.set(connectionId, entry);

    if (this.localStream) {
      for (const t of this.localStream.getTracks()) pc.addTrack(t, this.localStream);
    }

    pc.addEventListener('track', (ev) => {
      for (const t of ev.streams[0]?.getTracks() ?? [ev.track]) {
        if (!remoteStream.getTracks().includes(t)) remoteStream.addTrack(t);
      }
      this.publishRemotes();
    });

    pc.addEventListener('icecandidate', (ev) => {
      if (ev.candidate && this.oturumId) {
        this.hub.callSignal(this.oturumId, connectionId, 'ice', JSON.stringify(ev.candidate))
          .catch(() => undefined);
      }
    });

    pc.addEventListener('connectionstatechange', () => {
      this.publishRemotes();
      if (pc.connectionState === 'failed') {
        // Otomatik yeniden müzakere — basit ICE restart denemesi.
        try { pc.restartIce(); } catch { /* noop */ }
      }
    });

    pc.addEventListener('negotiationneeded', async () => {
      if (!this.oturumId) return;
      try {
        entry.makingOffer = true;
        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        await this.hub.callSignal(this.oturumId, connectionId, 'offer', JSON.stringify(pc.localDescription));
      } catch (err) {
        console.warn('[call] negotiationneeded failed', err);
      } finally {
        entry.makingOffer = false;
      }
    });

    this.publishRemotes();
    return entry;
  }

  private async handleSignal(
    fromConnectionId: string,
    kullaniciId: string,
    kullaniciAdi: string,
    type: string,
    payload: string,
  ): Promise<void> {
    const entry = await this.ensurePeer(fromConnectionId, kullaniciId, kullaniciAdi, /*initiate*/ false);
    const pc = entry.pc;
    if (!this.oturumId) return;

    try {
      if (type === 'offer') {
        const desc = JSON.parse(payload) as RTCSessionDescriptionInit;
        const offerCollision = entry.makingOffer || pc.signalingState !== 'stable';
        entry.ignoreOffer = !entry.polite && offerCollision;
        if (entry.ignoreOffer) return;

        if (offerCollision) {
          await Promise.all([
            pc.setLocalDescription({ type: 'rollback' } as RTCSessionDescriptionInit).catch(() => undefined),
            pc.setRemoteDescription(desc),
          ]);
        } else {
          await pc.setRemoteDescription(desc);
        }
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        await this.hub.callSignal(this.oturumId, fromConnectionId, 'answer', JSON.stringify(pc.localDescription));
      } else if (type === 'answer') {
        const desc = JSON.parse(payload) as RTCSessionDescriptionInit;
        if (pc.signalingState !== 'stable') {
          await pc.setRemoteDescription(desc);
        }
      } else if (type === 'ice') {
        const cand = JSON.parse(payload) as RTCIceCandidateInit;
        try { await pc.addIceCandidate(cand); }
        catch (err) { if (!entry.ignoreOffer) console.warn('[call] addIceCandidate failed', err); }
      }
    } catch (err) {
      console.error('[call] handleSignal error', err);
    }
  }

  private publishRemotes(): void {
    const list: RemotePeer[] = [];
    for (const [cid, entry] of this.peers.entries()) {
      list.push({
        connectionId: cid,
        kullaniciId: entry.kullaniciId,
        kullaniciAdi: entry.kullaniciAdi,
        stream: entry.stream,
        connectionState: entry.pc.connectionState,
      });
    }
    this.remotes.set(list);
  }

  private humanizeError(err: unknown): string {
    const e = err as { name?: string; message?: string };
    const name = e?.name ?? '';
    if (name === 'NotAllowedError') return 'Kamera/mikrofon izni reddedildi. Tarayıcı ayarlarından izin ver.';
    if (name === 'NotFoundError') return 'Kamera veya mikrofon bulunamadı.';
    if (name === 'NotReadableError') return 'Cihaz başka bir uygulama tarafından kullanılıyor.';
    if (name === 'OverconstrainedError') return 'Seçili cihaz desteklenmiyor.';
    return e?.message || 'Görüşme başlatılamadı.';
  }
}
