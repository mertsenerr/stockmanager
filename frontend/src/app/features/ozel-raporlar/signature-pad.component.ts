import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  ViewChild,
} from '@angular/core';

/**
 * Basit canvas tabanlı imza pad'i. Mouse ve touch input destekler, PNG data URI
 * üretir. UX mock için yeterli — gerçek e-imza değil.
 */
@Component({
  selector: 'app-signature-pad',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sp-wrap">
      <canvas
        #cnv
        class="sp-canvas"
        [width]="width"
        [height]="height"
        (pointerdown)="onPointerDown($event)"
        (pointermove)="onPointerMove($event)"
        (pointerup)="onPointerUp($event)"
        (pointerleave)="onPointerUp($event)"
        (pointercancel)="onPointerUp($event)"
      ></canvas>
      <div class="sp-baseline" aria-hidden="true"></div>
      <p class="sp-hint">Fareyle (veya parmağınızla) imzanızı buraya çizin.</p>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .sp-wrap {
      position: relative;
      border: 1px solid var(--color-border, rgba(0,0,0,0.1));
      border-radius: 8px;
      background: var(--color-surface, #fff);
      overflow: hidden;
    }
    :host-context([data-theme="dark"]) .sp-wrap { background: #fff; }
    .sp-canvas {
      display: block;
      width: 100%;
      height: 160px;
      touch-action: none;
      cursor: crosshair;
    }
    .sp-baseline {
      position: absolute;
      left: 12%; right: 12%;
      bottom: 32px;
      border-bottom: 1px dashed rgba(0,0,0,0.25);
      pointer-events: none;
    }
    .sp-hint {
      position: absolute;
      bottom: 6px; left: 0; right: 0;
      text-align: center;
      font-size: 10px;
      color: rgba(0,0,0,0.4);
      pointer-events: none;
    }
  `],
})
export class SignaturePadComponent implements AfterViewInit {
  @Input() width = 500;
  @Input() height = 160;
  @Output() readonly changed = new EventEmitter<boolean>();

  @ViewChild('cnv', { static: true })
  private readonly canvasRef!: ElementRef<HTMLCanvasElement>;

  private ctx!: CanvasRenderingContext2D;
  private drawing = false;
  private lastX = 0;
  private lastY = 0;
  private hasInk = false;

  ngAfterViewInit(): void {
    const c = this.canvasRef.nativeElement;
    const ctx = c.getContext('2d');
    if (!ctx) return;
    this.ctx = ctx;
    this.ctx.lineWidth = 2.2;
    this.ctx.lineCap = 'round';
    this.ctx.lineJoin = 'round';
    this.ctx.strokeStyle = '#0a1a2f';
  }

  isEmpty(): boolean { return !this.hasInk; }

  clear(): void {
    const c = this.canvasRef.nativeElement;
    this.ctx.clearRect(0, 0, c.width, c.height);
    this.hasInk = false;
    this.changed.emit(false);
  }

  /** Boyalı pikselleri kapsayan minimum dikdörtgeni kırpıp PNG data URI üretir. */
  toDataUri(): string | null {
    if (!this.hasInk) return null;
    const c = this.canvasRef.nativeElement;
    const data = this.ctx.getImageData(0, 0, c.width, c.height).data;
    let minX = c.width, minY = c.height, maxX = 0, maxY = 0;
    for (let y = 0; y < c.height; y++) {
      for (let x = 0; x < c.width; x++) {
        const a = data[(y * c.width + x) * 4 + 3];
        if (a > 0) {
          if (x < minX) minX = x;
          if (x > maxX) maxX = x;
          if (y < minY) minY = y;
          if (y > maxY) maxY = y;
        }
      }
    }
    if (maxX <= minX || maxY <= minY) return null;

    const pad = 6;
    minX = Math.max(0, minX - pad);
    minY = Math.max(0, minY - pad);
    maxX = Math.min(c.width, maxX + pad);
    maxY = Math.min(c.height, maxY + pad);
    const w = maxX - minX;
    const h = maxY - minY;

    const out = document.createElement('canvas');
    out.width = w;
    out.height = h;
    const outCtx = out.getContext('2d');
    if (!outCtx) return c.toDataURL('image/png');
    outCtx.drawImage(c, minX, minY, w, h, 0, 0, w, h);
    return out.toDataURL('image/png');
  }

  protected onPointerDown(ev: PointerEvent): void {
    ev.preventDefault();
    const c = this.canvasRef.nativeElement;
    c.setPointerCapture(ev.pointerId);
    const { x, y } = this.toLocal(ev);
    this.drawing = true;
    this.lastX = x;
    this.lastY = y;
    // Tek tıkla bir nokta bırak — kullanıcı dot dokunma yapabilsin.
    this.ctx.beginPath();
    this.ctx.arc(x, y, 1.1, 0, Math.PI * 2);
    this.ctx.fillStyle = '#0a1a2f';
    this.ctx.fill();
    if (!this.hasInk) {
      this.hasInk = true;
      this.changed.emit(true);
    }
  }

  protected onPointerMove(ev: PointerEvent): void {
    if (!this.drawing) return;
    ev.preventDefault();
    const { x, y } = this.toLocal(ev);
    this.ctx.beginPath();
    this.ctx.moveTo(this.lastX, this.lastY);
    this.ctx.lineTo(x, y);
    this.ctx.stroke();
    this.lastX = x;
    this.lastY = y;
  }

  protected onPointerUp(ev: PointerEvent): void {
    if (!this.drawing) return;
    this.drawing = false;
    const c = this.canvasRef.nativeElement;
    try { c.releasePointerCapture(ev.pointerId); } catch { /* zaten kapalı */ }
  }

  private toLocal(ev: PointerEvent): { x: number; y: number } {
    const c = this.canvasRef.nativeElement;
    const rect = c.getBoundingClientRect();
    // Canvas'ın internal genişliği (attribute) CSS genişliğinden farklı olabilir.
    const scaleX = c.width / rect.width;
    const scaleY = c.height / rect.height;
    return {
      x: (ev.clientX - rect.left) * scaleX,
      y: (ev.clientY - rect.top) * scaleY,
    };
  }
}
