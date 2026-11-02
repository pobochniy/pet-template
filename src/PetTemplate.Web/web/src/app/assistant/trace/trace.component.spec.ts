import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { AssistantTraceComponent } from './trace.component';
import { AssistantApiService } from '../../shared/api/assistant-api.service';
import { AssistantApiServiceMock } from '../../shared/api/assistant-api.service.mock';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';

/** «Показать ход мысли» (спец. assistant, 13.2) */
describe('AssistantTraceComponent', () => {
  let fixture: ComponentFixture<AssistantTraceComponent>;
  let api: AssistantApiServiceMock;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantTraceComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        AssistantApiServiceMock,
        { provide: AssistantApiService, useExisting: AssistantApiServiceMock }
      ]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));
    api = TestBed.inject(AssistantApiServiceMock);

    fixture = TestBed.createComponent(AssistantTraceComponent);
    fixture.componentRef.setInput('runId', 'run-1');
    fixture.detectChanges();
  });

  const element = () => fixture.nativeElement as HTMLElement;

  it('should unfold the steps on click and fold them on the second', async () => {
    api.setTrace({
      expired: false,
      steps: [
        { step: 1, kind: 'model', summary: '', durationMs: 1500 },
        { step: 2, kind: 'tool', toolName: 'search_help', summary: 'искал в справке: как пригласить коллегу', durationMs: 120 }
      ]
    });

    (element().querySelector('.trace__toggle') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element().textContent).toContain('искал в справке: как пригласить коллегу');
    expect(element().textContent).toContain('думаю');
    expect(element().textContent).toContain('1,5 с');
    expect(element().textContent).toContain('Скрыть ход мысли');

    await fixture.componentInstance.toggle();
    fixture.detectChanges();
    expect(element().textContent).not.toContain('искал в справке');
  });

  it('should say the details are gone for a cleaned trace, not show emptiness', async () => {
    api.setTrace({ expired: true, steps: [] });

    await fixture.componentInstance.toggle();
    fixture.detectChanges();

    expect(element().textContent).toContain('Подробности уже не хранятся');
  });

  it('should show a failed step in red with its reason', async () => {
    api.setTrace({
      expired: false,
      steps: [{ step: 1, kind: 'tool', toolName: 'search_help', summary: 'искал в справке', durationMs: 50, error: 'справка недоступна' }]
    });

    await fixture.componentInstance.toggle();
    fixture.detectChanges();

    const step = element().querySelector('.trace__step');
    expect(step?.classList).toContain('text-danger');
    expect(step?.textContent).toContain('справка недоступна');
  });
});
