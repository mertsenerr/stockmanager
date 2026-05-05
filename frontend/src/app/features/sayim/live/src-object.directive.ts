import { Directive, ElementRef, Input, OnChanges, inject } from '@angular/core';

/** <video [appSrcObject]="stream"> — MediaStream'i HTMLMediaElement.srcObject'e bağlar. */
@Directive({
  selector: '[appSrcObject]',
  standalone: true,
})
export class SrcObjectDirective implements OnChanges {
  private readonly el = inject(ElementRef<HTMLMediaElement>);
  @Input() appSrcObject: MediaStream | null = null;

  ngOnChanges(): void {
    const node = this.el.nativeElement;
    if (node.srcObject !== this.appSrcObject) {
      node.srcObject = this.appSrcObject;
      // iOS Safari user-gesture sonrası play() çağrısı.
      node.play?.().catch(() => undefined);
    }
  }
}
