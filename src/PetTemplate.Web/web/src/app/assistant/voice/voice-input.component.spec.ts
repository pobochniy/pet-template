import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { VoiceInputComponent } from './voice-input.component';
import { AssistantService } from '../assistant.service';
import { AssistantApiService } from '../../shared/api/assistant-api.service';
import { AssistantApiServiceMock } from '../../shared/api/assistant-api.service.mock';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { WorkspaceContextService } from '../../shared/core/workspace-context.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';

/**
 * Голос (спец. assistant, 6): наговорённое попадает в поле ввода, а не в тред.
 *
 * Запись подменена: настоящий `MediaRecorder` в headless-браузере микрофона
 * не найдёт, а проверяется здесь не он, а наш обвес вокруг него.
 */
describe('VoiceInputComponent', () => {
  let fixture: ComponentFixture<VoiceInputComponent>;
  let component: VoiceInputComponent;
  let api: AssistantApiServiceMock;
  let recorder: FakeRecorder;
  let denied: boolean;

  const scope = window as unknown as { MediaRecorder: unknown };
  const originalRecorder = scope.MediaRecorder;
  const originalMedia = navigator.mediaDevices;

  beforeEach(async () => {
    recorder = new FakeRecorder();
    denied = false;
    const fake = function () {
      return recorder;
    } as unknown as { isTypeSupported: (type: string) => boolean };
    fake.isTypeSupported = () => true;
    scope.MediaRecorder = fake;

    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: {
        getUserMedia: async () => {
          if (denied) throw new DOMException('denied', 'NotAllowedError');

          return new FakeStream() as unknown as MediaStream;
        }
      }
    });

    await TestBed.configureTestingModule({
      imports: [VoiceInputComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        AssistantApiServiceMock,
        { provide: AssistantApiService, useExisting: AssistantApiServiceMock }
      ]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));
    api = TestBed.inject(AssistantApiServiceMock);
    TestBed.inject(WorkspaceContextService).setCurrentWorkspaceId('ws-1');

    fixture = TestBed.createComponent(VoiceInputComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    scope.MediaRecorder = originalRecorder;
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: originalMedia });
    TestBed.inject(WorkspaceContextService).setCurrentWorkspaceId(null);
  });

  const element = () => fixture.nativeElement as HTMLElement;

  it('should hand the transcription out as text and send nothing to the thread', async () => {
    api.transcription = 'как сменить язык';
    const heard: string[] = [];
    component.text.subscribe(text => heard.push(text));

    await component.toggle();
    expect(component.recording()).toBeTrue();

    await recorder.finish();
    await fixture.whenStable();

    expect(heard).toEqual(['как сменить язык']);
    expect(api.transcribed).toEqual([1]);
    expect(api.sent).toEqual([]);
  });

  it('should show the stop button and the cancel button while recording', async () => {
    await component.toggle();
    fixture.detectChanges();

    expect(element().querySelector('#voice-toggle')?.getAttribute('aria-label')).toBe('Закончить запись');
    expect(element().querySelector('#voice-cancel')).not.toBeNull();
  });

  it('should send nothing on cancel', async () => {
    const heard: string[] = [];
    component.text.subscribe(text => heard.push(text));

    await component.toggle();
    component.cancel();
    await recorder.finish();
    await fixture.whenStable();

    expect(heard).toEqual([]);
    expect(api.transcribed).toEqual([]);
  });

  it('should say plainly that the microphone is blocked', async () => {
    denied = true;

    await component.toggle();

    expect(component.recording()).toBeFalse();
    expect(TestBed.inject(AlertsService).alerts()[0].content).toContain('Микрофон запрещён');
  });

  it('should stop by itself after a minute', async () => {
    jasmine.clock().install();
    try {
      await component.toggle();

      jasmine.clock().tick(60_000);

      expect(recorder.stopped).toBeTrue();
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('should show the refusal in words when recognition is not set up', async () => {
    api.refuseTranscribe('transcriptionUnavailable');

    await component.toggle();
    await recorder.finish();
    await fixture.whenStable();

    expect(TestBed.inject(AlertsService).alerts()[0].content).toBe('Распознавание речи не настроено');
  });

  /** Голос считается в лимит пространства (6, 13.6) — ответ тот же, что у отправки */
  it('should put up the workspace limit plate instead of an alert', async () => {
    api.refuseTranscribe('dailyLimitReached', 'Семья Ивановых');

    await component.toggle();
    await recorder.finish();
    await fixture.whenStable();

    expect(TestBed.inject(AssistantService).limitReached()).toBeTrue();
    expect(TestBed.inject(AlertsService).alerts()).toEqual([]);
  });
});

/** Запись, которой можно сказать «готово» из теста */
class FakeRecorder {
  public ondataavailable: ((event: { data: Blob }) => void) | null = null;
  public onstop: (() => void) | null = null;
  public readonly mimeType = 'audio/webm';
  public stopped = false;

  public start() {
    this.stopped = false;
  }

  public stop() {
    this.stopped = true;
    this.ondataavailable?.({ data: new Blob(['sound']) });
    this.onstop?.();
  }

  /** Остановка и ожидание расшифровки */
  public async finish() {
    this.stop();
    await Promise.resolve();
  }
}

class FakeStream {
  getTracks() {
    return [{ stop: () => undefined }];
  }
}
