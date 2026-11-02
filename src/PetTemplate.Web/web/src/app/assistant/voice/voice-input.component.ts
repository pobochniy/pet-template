import {
  ChangeDetectionStrategy, Component, ElementRef, OnDestroy, inject, output, signal, viewChild
} from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { Mic, Square, X } from 'lucide-angular/src/icons';
import { AssistantApiService } from '../../shared/api/assistant-api.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { serverErrorMessage } from '../../shared/core/server-error.util';
import { provideIcons } from '../../shared/icons/lucide-icons.provider';
import { AssistantService } from '../assistant.service';

/** Сколько держится всплывающее сообщение о записи */
const ALERT_MS = 5000;

/**
 * Голосовой ввод (спец. assistant, 6).
 *
 * <b>Наговорил → текст появился в поле → человек видит и правит → отправляет сам.</b>
 * Расшифровка ошибается в именах и числах, а уходить в тред должно то, что человек
 * видел глазами, — поэтому компонент отдаёт текст наружу и ничего не отправляет.
 *
 * Только в чате: строка на главной лежит в начальной сборке, а запись звука ей
 * не нужна настолько, чтобы за неё платить каждым заходом.
 *
 * Контейнер и кодек выбираются по тому, что умеет браузер: на iOS Safari это
 * не webm/opus, и сервер принимает то, что пришло.
 */
@Component({
  selector: 'app-voice-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideAngularModule],
  providers: [provideIcons({ Mic, Square, X })],
  template: `
    @if (supported) {
      <div class="voice d-flex align-items-center gap-1">
        @if (recording()) {
          <button type="button" id="voice-cancel" class="btn btn-sm btn-link text-body-secondary p-1"
                  [attr.aria-label]="i18n.t('assistant.voice.cancel')" (click)="cancel()">
            <i-lucide name="x" [size]="16" aria-hidden="true"></i-lucide>
          </button>
        }

        <button type="button" id="voice-toggle" class="btn btn-outline-secondary position-relative flex-shrink-0"
                [class.btn-outline-danger]="recording()"
                [disabled]="busy()"
                [attr.aria-label]="i18n.t(recording() ? 'assistant.voice.stop' : 'assistant.voice.record')"
                [attr.title]="i18n.t(recording() ? 'assistant.voice.stop' : 'assistant.voice.record')"
                (click)="toggle()">
          @if (busy()) {
            <span class="spinner-border spinner-border-sm" aria-hidden="true"></span>
          } @else if (recording()) {
            <i-lucide name="square" [size]="18" aria-hidden="true"></i-lucide>
            <!-- Уровень звука, а не просто «идёт запись»: человек должен видеть,
                 что его слышно (6.3). Ширину полоски ставит код кадром анимации -->
            <span #level class="voice__level"></span>
          } @else {
            <i-lucide name="mic" [size]="18" aria-hidden="true"></i-lucide>
          }
        </button>
      </div>
    }
  `,
  styles: [`
    .voice__level {
      position: absolute;
      left: 0.25rem;
      right: 0.25rem;
      bottom: 0.15rem;
      height: 2px;
      background: var(--bs-danger);
      transform: scaleX(0);
      transform-origin: left center;
      transition: transform 0.1s linear;
    }
  `]
})
export class VoiceInputComponent implements OnDestroy {
  /** Потолок записи — минута (6.3): дальше это уже не реплика. Сервер держит тот же */
  public static readonly MaxMs = 60_000;

  private static readonly MimeTypes = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/ogg'];

  protected readonly i18n = inject(I18nService);
  private readonly api = inject(AssistantApiService);
  private readonly alerts = inject(AlertsService);
  private readonly assistant = inject(AssistantService);

  /** Расшифровка — в поле ввода, а не в тред */
  public readonly text = output<string>();

  public readonly recording = signal(false);

  public readonly busy = signal(false);

  /**
   * Полоска громкости. Ширина ставится прямо элементу, а не привязкой стиля: шестьдесят
   * кадров в секунду незачем гнать через перерисовку, а инструкция стилевой привязки
   * впервые вынеслась бы из начального чанка Angular наружу — initial рос бы ради
   * одной полоски (tech-debt/bundle.md)
   */
  private readonly levelBar = viewChild<ElementRef<HTMLElement>>('level');

  /** Без `MediaRecorder` кнопки просто нет: обещать запись и не уметь — хуже */
  public readonly supported = typeof MediaRecorder !== 'undefined'
    && typeof navigator !== 'undefined'
    && navigator.mediaDevices?.getUserMedia != null;

  private recorder: MediaRecorder | null = null;
  private chunks: Blob[] = [];
  private stream: MediaStream | null = null;
  private audio: AudioContext | null = null;
  private meter: number | null = null;
  private limit: ReturnType<typeof setTimeout> | null = null;
  private startedAt = 0;
  private cancelled = false;

  public ngOnDestroy() {
    // Ушли с экрана посреди записи — микрофон гасим, на сервер не шлём ничего
    this.cancelled = true;
    this.cleanup();
  }

  public async toggle() {
    if (this.recording()) {
      this.recorder?.stop();

      return;
    }

    await this.start();
  }

  /** Отмена: запись выбрасывается, на сервер не уходит ничего */
  public cancel() {
    this.cancelled = true;
    this.recorder?.stop();
  }

  private async start() {
    try {
      this.stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch {
      // Микрофон запрещён — говорим об этом прямо, а не молчим (6.3)
      this.alerts.push('warning', this.i18n.t('assistant.voice.denied'), ALERT_MS);

      return;
    }

    this.cancelled = false;
    this.chunks = [];
    this.recorder = new MediaRecorder(this.stream, this.options());
    this.recorder.ondataavailable = event => {
      if (event.data.size > 0) this.chunks.push(event.data);
    };
    this.recorder.onstop = () => void this.finish();

    this.recorder.start();
    this.startedAt = Date.now();
    this.recording.set(true);
    this.listenLevel();

    this.limit = setTimeout(() => {
      if (!this.recording()) return;

      this.alerts.push('info', this.i18n.t('assistant.voice.tooLong'), ALERT_MS);
      this.recorder?.stop();
    }, VoiceInputComponent.MaxMs);
  }

  private async finish() {
    const type = this.recorder?.mimeType || 'audio/webm';
    const blob = new Blob(this.chunks, { type });
    // Округление вверх и не больше потолка: таймер срабатывает на миллисекунды позже,
    // и минута записи не должна превратиться в отказ «слишком длинно»
    const seconds = Math.min(
      Math.max(1, Math.ceil((Date.now() - this.startedAt) / 1000)),
      VoiceInputComponent.MaxMs / 1000);
    this.cleanup();

    if (this.cancelled || blob.size === 0) return;

    this.busy.set(true);
    try {
      const text = await this.api.Transcribe(blob, `voice.${this.extension(type)}`, seconds);
      if (text.trim().length > 0) this.text.emit(text.trim());
    } catch (e) {
      // Лимит пространства — та же плашка, что у отправки: голос считается в тот же лимит
      if (this.assistant.refusedByLimit(e)) return;

      this.alerts.push(
        'danger',
        serverErrorMessage(e, this.i18n, this.i18n.t('assistant.error.transcriptionFailed')),
        ALERT_MS);
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Уровень звука из `AnalyserNode`. Нужен не для красоты: без него человек
   * не отличит «записывается» от «микрофон молчит» (6.3)
   */
  private listenLevel() {
    if (typeof AudioContext === 'undefined' || !this.stream) return;

    let analyser: AnalyserNode;
    try {
      this.audio = new AudioContext();
      analyser = this.audio.createAnalyser();
      analyser.fftSize = 512;
      this.audio.createMediaStreamSource(this.stream).connect(analyser);
    } catch {
      // Полоска уровня — украшение поверх записи, и ронять запись она не вправе
      this.audio = null;

      return;
    }

    const samples = new Uint8Array(analyser.frequencyBinCount);
    const tick = () => {
      analyser.getByteTimeDomainData(samples);
      let peak = 0;
      for (const sample of samples) peak = Math.max(peak, Math.abs(sample - 128) / 128);
      const bar = this.levelBar()?.nativeElement;
      if (bar) bar.style.transform = `scaleX(${Math.min(1, peak * 2)})`;

      if (this.recording()) this.meter = requestAnimationFrame(tick);
    };
    this.meter = requestAnimationFrame(tick);
  }

  private options(): MediaRecorderOptions {
    const supported = VoiceInputComponent.MimeTypes.find(type => MediaRecorder.isTypeSupported?.(type));

    return supported ? { mimeType: supported } : {};
  }

  private extension(type: string) {
    if (type.includes('mp4')) return 'm4a';
    if (type.includes('ogg')) return 'ogg';

    return 'webm';
  }

  private cleanup() {
    this.recording.set(false);

    if (this.limit) clearTimeout(this.limit);
    this.limit = null;

    if (this.meter != null) cancelAnimationFrame(this.meter);
    this.meter = null;

    this.stream?.getTracks().forEach(track => track.stop());
    this.stream = null;

    void this.audio?.close().catch(() => undefined);
    this.audio = null;
    this.recorder = null;
  }
}
