import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { SearchResultItem, SearchResults, SearchService } from '../../../core/services/search.service';

interface FlatResultRef {
  flatIndex: number;
  item: SearchResultItem;
  category: keyof SearchResults;
}

const CATEGORY_LABEL: Record<keyof SearchResults, string> = {
  firmalar: 'Firmalar',
  magazalar: 'Mağazalar',
  oturumlar: 'Oturumlar',
  kullanicilar: 'Kullanıcılar',
};

@Component({
  selector: 'app-command-palette',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './command-palette.component.html',
  styleUrl: './command-palette.component.css',
})
export class CommandPaletteComponent implements OnChanges, AfterViewInit {
  private readonly searchSvc = inject(SearchService);
  private readonly router = inject(Router);

  @Input() open = false;
  @Output() closed = new EventEmitter<void>();

  @ViewChild('queryInput') queryInput?: ElementRef<HTMLInputElement>;

  readonly query = signal('');
  readonly loading = signal(false);
  readonly results = signal<SearchResults>({
    firmalar: [], magazalar: [], oturumlar: [], kullanicilar: [],
  });
  readonly cursor = signal(0);

  readonly flat = computed<FlatResultRef[]>(() => {
    const r = this.results();
    const out: FlatResultRef[] = [];
    let i = 0;
    const cats: (keyof SearchResults)[] = ['oturumlar', 'magazalar', 'firmalar', 'kullanicilar'];
    for (const c of cats) {
      for (const item of r[c]) {
        out.push({ flatIndex: i++, item, category: c });
      }
    }
    return out;
  });

  readonly hasResults = computed(() => this.flat().length > 0);
  readonly categoryLabel = (c: keyof SearchResults) => CATEGORY_LABEL[c];

  private readonly query$ = new Subject<string>();
  private querySub?: Subscription;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['open']) {
      if (this.open) {
        // Reset state when opened
        this.query.set('');
        this.results.set({ firmalar: [], magazalar: [], oturumlar: [], kullanicilar: [] });
        this.cursor.set(0);
        queueMicrotask(() => this.queryInput?.nativeElement.focus());
      }
    }
  }

  ngAfterViewInit(): void {
    this.querySub = this.query$
      .pipe(debounceTime(220))
      .subscribe((q) => this.runSearch(q));
  }

  ngOnDestroy(): void {
    this.querySub?.unsubscribe();
  }

  onInput(value: string): void {
    this.query.set(value);
    this.cursor.set(0);
    this.query$.next(value);
  }

  private runSearch(q: string): void {
    if (q.trim().length < 2) {
      this.results.set({ firmalar: [], magazalar: [], oturumlar: [], kullanicilar: [] });
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.searchSvc.search(q).subscribe({
      next: (r) => {
        this.results.set(r);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  onKeydown(ev: KeyboardEvent): void {
    if (ev.key === 'Escape') {
      ev.preventDefault();
      this.close();
      return;
    }
    const items = this.flat();
    if (items.length === 0) return;
    if (ev.key === 'ArrowDown') {
      ev.preventDefault();
      this.cursor.set((this.cursor() + 1) % items.length);
    } else if (ev.key === 'ArrowUp') {
      ev.preventDefault();
      this.cursor.set((this.cursor() - 1 + items.length) % items.length);
    } else if (ev.key === 'Enter') {
      ev.preventDefault();
      const idx = this.cursor();
      const target = items[idx];
      if (target) this.activate(target);
    }
  }

  activate(ref: FlatResultRef): void {
    const route = ref.item.route ?? this.defaultRoute(ref.category);
    this.router.navigateByUrl(route);
    this.close();
  }

  private defaultRoute(cat: keyof SearchResults): string {
    switch (cat) {
      case 'firmalar': return '/firmalar';
      case 'magazalar': return '/magazalar';
      case 'oturumlar': return '/oturumlar';
      case 'kullanicilar': return '/kullanicilar';
    }
  }

  close(): void { this.closed.emit(); }
  onBackdropClick(): void { this.close(); }
}
