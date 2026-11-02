import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { HomeAssistantInputComponent } from './assistant-input.component';
import { AssistantApiService } from '../../shared/api/assistant-api.service';
import { AssistantApiServiceMock } from '../../shared/api/assistant-api.service.mock';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';
import { AssistantService } from '../../assistant/assistant.service';
import { WorkspaceContextService } from '../../shared/core/workspace-context.service';

/**
 * Строка ввода на главной (спец. assistant, 2): набранное едет с собой
 * и отправляется первым сообщением
 */
describe('HomeAssistantInputComponent', () => {
  let fixture: ComponentFixture<HomeAssistantInputComponent>;
  let component: HomeAssistantInputComponent;
  let router: Router;
  let workspace: WorkspaceContextService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HomeAssistantInputComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        AssistantApiServiceMock,
        { provide: AssistantApiService, useExisting: AssistantApiServiceMock }
      ]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));
    workspace = TestBed.inject(WorkspaceContextService);
    workspace.setCurrentWorkspaceId('ws-ivanovy');
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);

    fixture = TestBed.createComponent(HomeAssistantInputComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => workspace.setCurrentWorkspaceId(null));

  const sendButton = () => fixture.nativeElement.querySelector('#home-assistant-send') as HTMLButtonElement;

  it('should go to the chat on Enter and take the text along', () => {
    component.draft.set('как пригласить коллегу');

    component.onKeydown(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(router.navigate).toHaveBeenCalledWith(['/assistant'], { state: { assistantText: 'как пригласить коллегу' } });
    expect(component.draft()).toBe('');
  });

  it('should go to the chat on «Send» typed by hand', () => {
    const input = fixture.nativeElement.querySelector('#home-assistant-input') as HTMLInputElement;
    input.value = 'как сменить язык';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    sendButton().click();

    expect(router.navigate).toHaveBeenCalledWith(['/assistant'], { state: { assistantText: 'как сменить язык' } });
  });

  /** Строка на главной — место, где пишут: клик по ней никуда не уводит */
  it('should not leave on a click into the line', () => {
    (fixture.nativeElement.querySelector('#home-assistant-input') as HTMLInputElement).click();

    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('should keep «Send» disabled on an empty line', () => {
    expect(sendButton().disabled).toBeTrue();

    component.open();

    expect(router.navigate).not.toHaveBeenCalled();
  });

  /** Лимит пространства (13.6): строка сама не отправляет — плашку ставит общее состояние ассистента */
  describe('workspace limit', () => {
    beforeEach(async () => {
      TestBed.inject(AssistantApiServiceMock).refuseSend('monthlyLimitReached', 'Семья Ивановых');
      await TestBed.inject(AssistantService).send('как пригласить коллегу');
      fixture.detectChanges();
    });

    it('should replace the line with the plate after a refused Send', () => {
      const el: HTMLElement = fixture.nativeElement;

      expect(el.querySelector('#home-assistant-input')).toBeNull();
      expect(el.querySelector('app-limit-reached')?.textContent).toContain('месячный лимит');
    });

    it('should keep the line in another space: its limit is its own', () => {
      workspace.setCurrentWorkspaceId('ws-rabota');
      TestBed.tick();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('#home-assistant-input')).not.toBeNull();
      expect(fixture.nativeElement.querySelector('app-limit-reached')).toBeNull();
    });
  });
});
