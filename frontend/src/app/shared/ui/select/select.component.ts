import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  Output,
  ViewChild,
  computed,
  forwardRef,
  signal,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

export interface SelectOption<T extends string = string> {
  value: T;
  label: string;
  description?: string;
  disabled?: boolean;
}

/**
 * Custom select — kendi açılır panelini render eder, native <select>'in
 * OS-controlled popup'ını değiştirir. Trigger ve panel sürekli border'la
 * birleşik görünür (popup "ayrı pencere" hissi vermez).
 *
 * - ControlValueAccessor: formControlName + [(ngModel)] destekler
 * - (selectionChange): plain template binding için event de yayar
 * - Klavye: Esc/Enter/Arrow + Tab ile blur
 * - Click outside ile kapanır
 */
@Component({
  selector: 'app-select',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => SelectComponent), multi: true },
  ],
  template: `
    <div class="aps-wrap"
         [class.is-open]="open()"
         [class.is-disabled]="disabled()"
         [class.size-sm]="size === 'sm'"
         #wrap>
      <button
        #trigger
        type="button"
        class="aps-trigger"
        [disabled]="disabled()"
        [attr.aria-haspopup]="'listbox'"
        [attr.aria-expanded]="open()"
        (click)="toggle()"
        (keydown)="onTriggerKey($event)"
      >
        <span class="aps-label" [class.is-placeholder]="!selectedLabel()">
          {{ selectedLabel() ?? placeholder }}
        </span>
        <span class="aps-chevron" aria-hidden="true">▾</span>
      </button>

      @if (open()) {
        <div class="aps-panel" role="listbox">
          @if (options.length === 0) {
            <div class="aps-empty">Seçenek yok</div>
          } @else {
            <ul class="aps-options">
              @for (opt of options; track opt.value; let i = $index) {
                <li
                  role="option"
                  class="aps-option"
                  [class.is-selected]="opt.value === value()"
                  [class.is-active]="i === activeIndex()"
                  [class.is-disabled]="opt.disabled"
                  [attr.aria-selected]="opt.value === value()"
                  (click)="!opt.disabled && pick(opt)"
                  (mouseenter)="activeIndex.set(i)"
                >
                  <span class="aps-option-label">{{ opt.label }}</span>
                  @if (opt.description) {
                    <span class="aps-option-desc">{{ opt.description }}</span>
                  }
                </li>
              }
            </ul>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; position: relative; }

    .aps-wrap {
      position: relative;
      border: 1.5px solid var(--color-border-strong, rgba(0,0,0,0.22));
      border-radius: 10px;
      background: var(--color-surface-elevated, #fff);
      transition: border-color 140ms, box-shadow 140ms;
    }
    .aps-wrap.is-open {
      border-color: var(--color-accent, #0f766e);
      /* Açıkken trigger + panel tek gövde gibi görünsün diye alt köşeler düz. */
      border-bottom-left-radius: 0;
      border-bottom-right-radius: 0;
    }
    .aps-wrap.is-disabled { opacity: 0.6; pointer-events: none; }

    .aps-trigger {
      display: flex; align-items: center; justify-content: space-between;
      gap: 8px;
      width: 100%;
      padding: 10px 12px;
      background: transparent;
      border: none;
      color: var(--color-ink);
      font-size: 13px;
      font-family: inherit;
      text-align: left;
      cursor: pointer;
      border-radius: inherit;
    }
    .aps-trigger:focus-visible {
      outline: none;
    }
    .aps-wrap.size-sm .aps-trigger {
      padding: 6px 10px;
      font-size: 12px;
    }
    .aps-label {
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
      flex: 1; min-width: 0;
    }
    .aps-label.is-placeholder { color: var(--color-ink-muted); }
    .aps-chevron {
      flex-shrink: 0;
      color: var(--color-ink-muted);
      font-size: 10px;
      transition: transform 160ms;
    }
    .aps-wrap.is-open .aps-chevron { transform: rotate(180deg); }

    /* Panel — wrap'in altında, üst kenarda border yok (trigger ile birleşir). */
    .aps-panel {
      position: absolute;
      top: 100%;
      left: -1.5px;
      right: -1.5px;
      background: var(--color-surface-elevated, #fff);
      border: 1.5px solid var(--color-accent, #0f766e);
      border-top: none;
      border-bottom-left-radius: 10px;
      border-bottom-right-radius: 10px;
      max-height: 280px;
      overflow-y: auto;
      z-index: 100;
      box-shadow: 0 8px 20px -8px rgba(0, 0, 0, 0.25);
    }
    .aps-empty {
      padding: 14px 12px;
      font-size: 12px;
      color: var(--color-ink-muted);
      text-align: center;
    }
    .aps-options {
      list-style: none;
      margin: 0;
      padding: 4px;
      display: flex;
      flex-direction: column;
      gap: 1px;
    }
    .aps-option {
      display: flex; flex-direction: column;
      padding: 8px 10px;
      border-radius: 6px;
      cursor: pointer;
      color: var(--color-ink);
      font-size: 13px;
      transition: background 100ms;
    }
    .aps-option:hover,
    .aps-option.is-active {
      background: var(--color-surface, rgba(0,0,0,0.04));
    }
    .aps-option.is-selected {
      background: var(--color-accent-soft, rgba(15, 118, 110, 0.12));
      color: var(--color-accent, #0f766e);
      font-weight: 600;
    }
    .aps-option.is-disabled {
      opacity: 0.45;
      cursor: not-allowed;
    }
    .aps-option-desc {
      font-size: 11px;
      color: var(--color-ink-muted);
      margin-top: 1px;
    }

    /* Dark mode */
    :host-context([data-theme="dark"]) .aps-wrap {
      background: rgba(255, 255, 255, 0.04);
      border-color: rgba(255, 255, 255, 0.22);
    }
    :host-context([data-theme="dark"]) .aps-wrap.is-open {
      border-color: rgba(var(--color-accent-rgb, 20, 184, 166), 0.65);
      background: rgba(255, 255, 255, 0.06);
    }
    :host-context([data-theme="dark"]) .aps-panel {
      background: var(--color-surface-elevated, #1a1a1a);
      border-color: rgba(var(--color-accent-rgb, 20, 184, 166), 0.55);
      box-shadow: 0 12px 28px -8px rgba(0, 0, 0, 0.55);
    }
    :host-context([data-theme="dark"]) .aps-option:hover,
    :host-context([data-theme="dark"]) .aps-option.is-active {
      background: rgba(255, 255, 255, 0.06);
    }
    :host-context([data-theme="dark"]) .aps-option.is-selected {
      background: rgba(var(--color-accent-rgb, 20, 184, 166), 0.18);
      color: var(--color-ink);
    }
  `],
})
export class SelectComponent implements ControlValueAccessor {
  @Input() options: ReadonlyArray<SelectOption> = [];
  @Input() placeholder = 'Seçiniz…';
  @Input() size: 'sm' | 'md' = 'md';
  @Input({ alias: 'disabled' }) set disabledInput(v: boolean) { this._disabled.set(!!v); }
  /** Plain one-way value binding — (selectionChange) ile beraber FormsModule
   *  gerektirmeden kullanılabilir. formControlName / ngModel hâlâ çalışır. */
  @Input({ alias: 'value' }) set valueInput(v: string | null | undefined) {
    this._value.set(typeof v === 'string' ? v : v == null ? '' : String(v));
  }
  @Output() readonly selectionChange = new EventEmitter<string>();

  @ViewChild('wrap', { static: true }) private readonly wrapRef!: ElementRef<HTMLElement>;
  @ViewChild('trigger', { static: true }) private readonly triggerRef!: ElementRef<HTMLButtonElement>;

  private readonly _value = signal<string>('');
  readonly value = this._value.asReadonly();
  readonly open = signal(false);
  readonly activeIndex = signal<number>(-1);
  private readonly _disabled = signal(false);
  readonly disabled = this._disabled.asReadonly();

  readonly selectedLabel = computed(() => {
    const v = this._value();
    return this.options.find((o) => o.value === v)?.label;
  });

  private onChange: (v: string) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  // ─── ControlValueAccessor ─────────────────────────────────────────────
  writeValue(value: unknown): void {
    this._value.set(typeof value === 'string' ? value : value == null ? '' : String(value));
  }
  registerOnChange(fn: (v: string) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  setDisabledState(isDisabled: boolean): void { this._disabled.set(isDisabled); }

  // ─── Interaction ──────────────────────────────────────────────────────
  toggle(): void {
    if (this._disabled()) return;
    this.open.update((v) => !v);
    if (this.open()) {
      // Açılınca seçili seçeneği aktif yap, yoksa ilkini.
      const idx = this.options.findIndex((o) => o.value === this.value());
      this.activeIndex.set(idx >= 0 ? idx : 0);
    }
  }

  close(emitTouch = true): void {
    if (!this.open()) return;
    this.open.set(false);
    if (emitTouch) this.onTouched();
  }

  pick(opt: SelectOption): void {
    if (opt.disabled) return;
    this._value.set(opt.value);
    this.onChange(opt.value);
    this.selectionChange.emit(opt.value);
    this.close();
    this.triggerRef.nativeElement.focus();
  }

  protected onTriggerKey(ev: KeyboardEvent): void {
    if (this._disabled()) return;
    if (ev.key === 'Escape') {
      ev.preventDefault();
      this.close();
      return;
    }
    if (ev.key === 'ArrowDown' || ev.key === 'ArrowUp') {
      ev.preventDefault();
      if (!this.open()) {
        this.toggle();
        return;
      }
      const dir = ev.key === 'ArrowDown' ? 1 : -1;
      const n = this.options.length;
      if (n === 0) return;
      let i = (this.activeIndex() + dir + n) % n;
      // Disabled seçenekleri atla
      let safety = n;
      while (this.options[i].disabled && safety-- > 0) {
        i = (i + dir + n) % n;
      }
      this.activeIndex.set(i);
      return;
    }
    if (ev.key === 'Enter' || ev.key === ' ') {
      if (!this.open()) {
        ev.preventDefault();
        this.toggle();
        return;
      }
      const opt = this.options[this.activeIndex()];
      if (opt) {
        ev.preventDefault();
        this.pick(opt);
      }
      return;
    }
    if (ev.key === 'Tab') {
      // Tab close — focus naturally moves on.
      this.close();
    }
  }

  // Click-outside close
  @HostListener('document:pointerdown', ['$event'])
  onDocPointerDown(ev: PointerEvent): void {
    if (!this.open()) return;
    const target = ev.target as Node | null;
    if (target && !this.wrapRef.nativeElement.contains(target)) {
      this.close();
    }
  }
}
