import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { SearchBoxComponent } from './search-box.component';

/** Строка поиска (спец. search, 8): запрос — по «Найти», а не на каждую букву */
describe('SearchBoxComponent', () => {
  let fixture: ComponentFixture<SearchBoxComponent>;
  let searched: string[];
  let cleared: number;

  const el = () => fixture.nativeElement as HTMLElement;
  const box = () => el().querySelector('#search-query') as HTMLInputElement;

  async function type(text: string): Promise<void> {
    box().value = text;
    box().dispatchEvent(new Event('input'));
    await fixture.whenStable();
  }

  async function submit(): Promise<void> {
    (el().querySelector('#search-submit') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SearchBoxComponent],
      providers: [provideZonelessChangeDetection()]
    }).compileComponents();

    fixture = TestBed.createComponent(SearchBoxComponent);
    searched = [];
    cleared = 0;
    fixture.componentInstance.search.subscribe(query => searched.push(query));
    fixture.componentInstance.cleared.subscribe(() => cleared++);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('should emit the trimmed query only on submit', async () => {
    await type('  кран  ');
    expect(searched).toEqual([]);

    await submit();

    expect(searched).toEqual(['кран']);
  });

  it('should not search for an empty query', async () => {
    await type('   ');
    await submit();

    expect(searched).toEqual([]);
    expect(el().querySelector('#search-clear')).toBeNull();
  });

  it('should reset the results and the field', async () => {
    await type('кран');
    await submit();

    (el().querySelector('#search-clear') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(cleared).toBe(1);
    expect(box().value).toBe('');
    expect(el().querySelector('#search-clear')).toBeNull();
  });

  it('should label the field from the core dictionary', () => {
    expect(box().placeholder).toBe('Поиск');
  });
});
