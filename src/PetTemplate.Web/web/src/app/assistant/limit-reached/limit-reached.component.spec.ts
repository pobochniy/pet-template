import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { LimitReachedComponent } from './limit-reached.component';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';

/** Плашка исчерпанного лимита пространства (спец. assistant, 13.6) */
describe('LimitReachedComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LimitReachedComponent],
      providers: [provideZonelessChangeDetection()]
    }).compileComponents();
    await loadAllScopes(TestBed.inject(I18nService));
  });

  it('should show the refusal and explain that the limit is shared by the space', () => {
    const fixture = TestBed.createComponent(LimitReachedComponent);
    fixture.componentRef.setInput('text', 'В пространстве «Котики» исчерпан месячный лимит ассистента');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.textContent).toContain('«Котики»');
    expect(el.textContent).toContain('Лимит общий для всех участников пространства');
  });

  it('should be dismissed by «Got it»', () => {
    const fixture = TestBed.createComponent(LimitReachedComponent);
    fixture.componentRef.setInput('text', 'лимит');
    fixture.detectChanges();
    let dismissed = 0;
    fixture.componentInstance.dismiss.subscribe(() => dismissed++);

    (fixture.nativeElement.querySelector('.limit-reached__dismiss') as HTMLButtonElement).click();

    expect(dismissed).toBe(1);
  });
});
