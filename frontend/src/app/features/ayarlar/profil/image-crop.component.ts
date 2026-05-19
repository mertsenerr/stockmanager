import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Input,
  ViewChild,
  computed,
  signal,
} from '@angular/core';

/**
 * Canvas tabanlı kare/daire profil fotoğrafı kırpıcı.
 *
 * - 320px viewport (daire mask)
 * - Pointer ile sürükle, scroll/slider ile zoom (0.5x – 4x)
 * - toDataUri(): viewport içeriğini outputSize (240px) kareye render edip
 *   PNG data URI döner
 *
 * Native canvas + pointer events; dış kütüphane yok.
 */
@Component({
  selector: 'app-image-crop',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ic-wrap">
      <div
        class="ic-stage"
        #stage
        (pointerdown)="onPointerDown($event)"
        (pointermove)="onPointerMove($event)"
        (pointerup)="onPointerUp($event)"
        (pointerleave)="onPointerUp($event)"
        (pointercancel)="onPointerUp($event)"
        (wheel)="onWheel($event)"
      >
        <canvas #cnv [width]="viewport" [height]="viewport"></canvas>
        <div class="ic-mask" aria-hidden="true"></div>
      </div>

      <div class="ic-controls">
        <span class="ic-zoom-label">Zoom</span>
        <input
          type="range"
          [min]="effectiveMinScale()"
          [max]="effectiveMaxScale()"
          step="0.01"
          [value]="scale()"
          (input)="onScaleSlider($any($event.target).value)"
          class="ic-slider"
        />
        <button type="button" (click)="reset()" class="ic-reset">Sıfırla</button>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .ic-wrap { display: flex; flex-direction: column; gap: 12px; align-items: center; }
    .ic-stage {
      position: relative;
      width: 320px;
      height: 320px;
      overflow: hidden;
      background: #0a0a0a;
      border-radius: 12px;
      touch-action: none;
      cursor: grab;
    }
    .ic-stage:active { cursor: grabbing; }
    .ic-stage canvas {
      display: block;
      width: 100%;
      height: 100%;
    }
    /* Daire crop mask — viewport içinde kalan kısım net, dış kısım koyu */
    .ic-mask {
      position: absolute; inset: 0;
      pointer-events: none;
      background:
        radial-gradient(circle at center,
          transparent 0,
          transparent calc(50% - 1px),
          rgba(0,0,0,0.55) calc(50%),
          rgba(0,0,0,0.55) 100%);
      border-radius: inherit;
    }

    .ic-controls {
      display: flex; align-items: center; gap: 10px;
      width: 100%;
      max-width: 320px;
    }
    .ic-zoom-label {
      font-size: 11px;
      color: var(--color-ink-muted);
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }
    .ic-slider {
      flex: 1;
      accent-color: var(--color-accent, #14b8a6);
    }
    .ic-reset {
      font-size: 12px;
      padding: 4px 10px;
      border-radius: 6px;
      border: 1px solid var(--color-border, rgba(0,0,0,0.1));
      background: transparent;
      color: var(--color-ink-secondary);
      cursor: pointer;
    }
    .ic-reset:hover { background: var(--color-surface-elevated); }
  `],
})
export class ImageCropComponent implements AfterViewInit {
  /** Kırpılacak resim — data URI veya URL. Set edilince yeniden yüklenir. */
  @Input({ required: true }) set src(value: string) {
    this._src = value;
    if (this.ctx) this.loadImage();
  }
  get src(): string { return this._src; }

  @Input() viewport = 320;
  @Input() outputSize = 240;
  /** Maximum zoom relative to the "cover" baseline (e.g. 3 = 3× the fitted size). */
  @Input() maxZoomFactor = 3;

  @ViewChild('cnv', { static: true })
  private readonly canvasRef!: ElementRef<HTMLCanvasElement>;

  readonly scale = signal(1);
  /**
   * Resmin viewport'u tam dolduran "cover" ölçeği. Slider'ın sol ucu burası
   * (daha aşağı inince viewport içinde boş alan kalır, kullanmamız anlamsız).
   */
  readonly fitScale = signal(1);
  /** Slider'a verdiğimiz dinamik aralık. */
  readonly effectiveMinScale = computed(() => this.fitScale());
  readonly effectiveMaxScale = computed(() => this.fitScale() * this.maxZoomFactor);
  private _src = '';
  private ctx!: CanvasRenderingContext2D;
  private img: HTMLImageElement | null = null;
  private imgNaturalW = 0;
  private imgNaturalH = 0;
  // Resmin merkezinin viewport içindeki pozisyonu (viewport koordinatlarında)
  private cx = 0;
  private cy = 0;
  // Pointer drag state
  private dragging = false;
  private lastX = 0;
  private lastY = 0;

  ngAfterViewInit(): void {
    const c = this.canvasRef.nativeElement;
    const ctx = c.getContext('2d');
    if (!ctx) return;
    this.ctx = ctx;
    if (this._src) this.loadImage();
  }

  private loadImage(): void {
    const img = new Image();
    img.onload = () => {
      this.img = img;
      this.imgNaturalW = img.naturalWidth;
      this.imgNaturalH = img.naturalHeight;
      this.fitAndCenter();
      this.render();
    };
    img.src = this._src;
  }

  /** Resmi viewport'a "cover" şeklinde sığdır + merkeze al. */
  private fitAndCenter(): void {
    if (!this.img) return;
    const fit = Math.max(this.viewport / this.imgNaturalW, this.viewport / this.imgNaturalH);
    this.fitScale.set(fit);
    this.scale.set(fit);
    this.cx = this.viewport / 2;
    this.cy = this.viewport / 2;
  }

  reset(): void {
    this.fitAndCenter();
    this.render();
  }

  private render(): void {
    if (!this.ctx) return;
    const v = this.viewport;
    this.ctx.fillStyle = '#0a0a0a';
    this.ctx.fillRect(0, 0, v, v);
    if (!this.img) return;
    const s = this.scale();
    const w = this.imgNaturalW * s;
    const h = this.imgNaturalH * s;
    this.ctx.drawImage(this.img, this.cx - w / 2, this.cy - h / 2, w, h);
  }

  protected onPointerDown(ev: PointerEvent): void {
    ev.preventDefault();
    this.canvasRef.nativeElement.setPointerCapture(ev.pointerId);
    this.dragging = true;
    this.lastX = ev.clientX;
    this.lastY = ev.clientY;
  }

  protected onPointerMove(ev: PointerEvent): void {
    if (!this.dragging) return;
    ev.preventDefault();
    const dx = ev.clientX - this.lastX;
    const dy = ev.clientY - this.lastY;
    this.lastX = ev.clientX;
    this.lastY = ev.clientY;
    this.cx += dx;
    this.cy += dy;
    this.render();
  }

  protected onPointerUp(ev: PointerEvent): void {
    if (!this.dragging) return;
    this.dragging = false;
    try { this.canvasRef.nativeElement.releasePointerCapture(ev.pointerId); } catch { /* ok */ }
  }

  protected onWheel(ev: WheelEvent): void {
    ev.preventDefault();
    const delta = -ev.deltaY * 0.0015;
    this.applyScale(this.scale() + delta);
  }

  protected onScaleSlider(value: string): void {
    this.applyScale(parseFloat(value));
  }

  private applyScale(next: number): void {
    const clamped = Math.max(this.effectiveMinScale(), Math.min(this.effectiveMaxScale(), next));
    this.scale.set(clamped);
    this.render();
  }

  /** Viewport içindeki dairesel kırpımı outputSize kareye render eder. */
  toDataUri(mime: 'image/png' | 'image/jpeg' = 'image/png', quality = 0.92): string {
    const out = document.createElement('canvas');
    out.width = this.outputSize;
    out.height = this.outputSize;
    const oc = out.getContext('2d');
    if (!oc || !this.img) return '';
    // Viewport içeriğini outputSize'a scale ederek aktar.
    oc.drawImage(this.canvasRef.nativeElement, 0, 0, this.viewport, this.viewport,
      0, 0, this.outputSize, this.outputSize);
    return out.toDataURL(mime, quality);
  }
}
