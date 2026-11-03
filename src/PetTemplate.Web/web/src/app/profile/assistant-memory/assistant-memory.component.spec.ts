import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { AssistantMemoryComponent } from './assistant-memory.component';
import { AssistantApiService } from '../../shared/api/assistant-api.service';
import { AssistantApiServiceMock } from '../../shared/api/assistant-api.service.mock';
import { UserService } from '../../shared/core/user.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';
import { AssistantFact, AssistantFactKind } from '../../shared/models/assistant.model';
import { profileDto, sessionDto } from '../../shared/api/dto.mock';

/**
 * «Память ассистента» (спец. assistant, 12.5): память обязана быть видимой,
 * и неверный факт удаляется одним нажатием — после подтверждения
 */
describe('AssistantMemoryComponent', () => {
  let fixture: ComponentFixture<AssistantMemoryComponent>;
  let api: AssistantApiServiceMock;
  let user: UserService;

  const fact = (id: string, text: string, personal: boolean, sourceRunId: string | null = null): AssistantFact => ({
    id, kind: AssistantFactKind.Alias, text, personal, createdAtUtc: '2026-10-01T10:00:00Z', sourceRunId
  });

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AssistantMemoryComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        AssistantApiServiceMock,
        { provide: AssistantApiService, useExisting: AssistantApiServiceMock }
      ]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));
    api = TestBed.inject(AssistantApiServiceMock);
    // Память читается по пространству вкладки — оно бывает только у вошедшего
    user = TestBed.inject(UserService);
    user.applySession(sessionDto({
      token: 'token',
      profile: profileDto({ id: 'me', userName: 'Vasya', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true }),
      permissionsByWorkspace: { 'ws-1': [], 'ws-2': [] }
    }), 'ws-1');
  });

  afterEach(() => user.clearProfile());

  async function create() {
    fixture = TestBed.createComponent(AssistantMemoryComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const element = () => fixture.nativeElement as HTMLElement;

  it('should group facts into the space and me after the answer arrives', async () => {
    api.setMemory({
      shared: [fact('f1', '«домик» — это дача', false, 'run-1')],
      personal: [fact('f2', 'встречи обычно на час', true)]
    });

    await create();

    const shared = element().querySelector('[data-group="shared"]') as HTMLElement;
    const personal = element().querySelector('[data-group="personal"]') as HTMLElement;
    expect(shared.textContent).toContain('Про пространство');
    expect(shared.textContent).toContain('«домик» — это дача');
    expect(shared.querySelector('a')?.getAttribute('href')).toBe('/assistant');
    expect(personal.textContent).toContain('Про меня');
    expect(personal.textContent).toContain('встречи обычно на час');
    expect(element().querySelector('#memory-empty')).toBeNull();
  });

  it('should say nothing is remembered when memory is empty', async () => {
    await create();

    expect(element().querySelector('#memory-empty')?.textContent).toContain('Пока ничего не запомнил');
  });

  it('should remove a fact only after confirmation', async () => {
    api.setMemory({ shared: [fact('f1', '«Илья» — это Настя', false)], personal: [] });
    await create();

    (element().querySelector('.fact-forget') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(api.forgotten).toEqual([]);
    expect(element().textContent).toContain('Убрать этот факт?');

    (element().querySelector('.fact-forget-confirm') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.forgotten).toEqual(['f1']);
    expect(element().textContent).not.toContain('«Илья» — это Настя');
    expect(element().querySelector('#memory-empty')).not.toBeNull();
  });

  /** Память своя в каждом пространстве — как и тред (12.1) */
  it('should reload the memory of the other space after switching', async () => {
    api.setMemory({ shared: [fact('f1', 'факт первого', false)], personal: [] });
    await create();
    expect(element().textContent).toContain('факт первого');

    api.setMemory({ shared: [fact('f2', 'факт второго', false)], personal: [] });
    user.switchWorkspace('ws-2');
    TestBed.tick();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element().textContent).not.toContain('факт первого');
    expect(element().textContent).toContain('факт второго');
  });
});
