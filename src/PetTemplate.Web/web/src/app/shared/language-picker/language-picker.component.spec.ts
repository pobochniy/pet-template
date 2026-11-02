import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { LanguagePickerComponent } from './language-picker.component';
import { I18nService } from '../i18n/i18n.service';
import { LANG_STORAGE_KEY, SUPPORTED_LANGUAGES } from '../i18n/lang';

describe('LanguagePickerComponent', () => {
  let fixture: ComponentFixture<LanguagePickerComponent>;
  let i18n: I18nService;

  const el = () => fixture.nativeElement as HTMLElement;
  const items = () => Array.from(el().querySelectorAll('.language-picker__item')) as HTMLButtonElement[];

  const settle = async () => {
    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    localStorage.clear();
    localStorage.setItem(LANG_STORAGE_KEY, 'ru');

    await TestBed.configureTestingModule({
      imports: [LanguagePickerComponent],
      providers: [provideZonelessChangeDetection()]
    }).compileComponents();

    i18n = TestBed.inject(I18nService);

    fixture = TestBed.createComponent(LanguagePickerComponent);
    fixture.componentRef.setInput('isOpen', true);
    fixture.componentRef.setInput('selected', 'ru');
    fixture.detectChanges();
    await settle();
  });

  afterEach(() => localStorage.clear());

  it('should show every supported language by its native name', () => {
    expect(items().map(item => item.textContent?.trim())).toEqual(
      SUPPORTED_LANGUAGES.map(lang => lang.nativeName)
    );
  });

  it('should mark the current language', () => {
    const selected = items().filter(item => item.getAttribute('aria-pressed') === 'true');

    expect(selected.length).toBe(1);
    expect(selected[0].textContent?.trim()).toBe('Русский');
  });

  it('should hide the search field while there are few languages', () => {
    // Поле появится, когда языков станет больше горстки; логика фильтрации
    // написана сразу, скрыто только поле
    expect(el().querySelector('#input-language-search')).toBeNull();
  });

  it('should emit the picked code', async () => {
    let picked: string | undefined;
    fixture.componentInstance.picked.subscribe(lang => (picked = lang));

    items()[1].click();
    await settle();

    expect(picked).toBe('en');
  });

  it('should draw nothing while closed', async () => {
    fixture.componentRef.setInput('isOpen', false);
    await settle();

    expect(items().length).toBe(0);
  });

  it('should follow the interface language', async () => {
    await i18n.setLang('en');
    await settle();

    expect(el().querySelector('.language-picker__title')?.textContent?.trim())
      .toBe('Interface language');
  });
});
