import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { LucideAngularModule } from 'lucide-angular';
import { SendHorizontal, Sparkles } from 'lucide-angular/src/icons';
import { AssistantService } from '../../assistant/assistant.service';
import { LimitReachedComponent } from '../../assistant/limit-reached/limit-reached.component';
import { ASSISTANT_TEXT_STATE } from '../../assistant/assistant-text';
import { provideIcons } from '../../shared/icons/lucide-icons.provider';
import { injectI18n } from '../../shared/i18n/inject-i18n';
import { HOME_SCOPES } from '../home.scopes';

/**
 * Строка ввода ассистента на главной (спец. assistant, 2).
 *
 * Вход — <b>строка, а не кнопка</b>: кнопка была бы дублированием навигации
 * и удлиняла бы путь на экран.
 *
 * Фраза набирается <b>здесь</b>, на главной: экран чата открывается по «Отправить»
 * (или Enter) вместе с первым сообщением. Клик по строке никуда не уводит — иначе
 * человек не успевал бы дописать то, ради чего пришёл.
 *
 * Исчерпанный лимит пространства (13.6) заменяет строку плашкой на её месте. Сама
 * строка ничего не отправляет — отказ получает чат, куда она уводит, — и главная
 * узнаёт о нём из общего состояния ассистента.
 *
 * Лежит в чанке главной вместе с `AssistantService`: в начальный бандл не попадает
 * ничего (`tech-debt/bundle.md`)
 */
@Component({
  selector: 'home-assistant-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideAngularModule, LimitReachedComponent],
  providers: [provideIcons({ SendHorizontal, Sparkles })],
  template: `
    @if (assistant.limitReached()) {
      <app-limit-reached class="home-assistant d-block p-2 mb-3 border rounded-3"
                         [text]="assistant.limitText()" (dismiss)="assistant.dismissLimit()" />
    } @else {
      <div class="home-assistant d-flex align-items-center gap-2 p-2 mb-3 border rounded-3">
        <i-lucide name="sparkles" [size]="18" class="text-primary flex-shrink-0" aria-hidden="true"></i-lucide>

        <input id="home-assistant-input" class="form-control border-0 shadow-none"
               [attr.placeholder]="i18n.t('assistant.input.home')"
               [attr.aria-label]="i18n.t('assistant.input.home')"
               [attr.maxlength]="4000"
               [value]="draft()"
               (input)="onInput($event)"
               (keydown)="onKeydown($event)"/>

        <!-- На узком экране остаётся одна иконка: подпись «Отправить» забирала ширину
             у самой строки. Подпись возвращается с брейкпоинта sm -->
        <button type="button" id="home-assistant-send" class="btn btn-primary flex-shrink-0"
                [disabled]="draft().trim().length === 0"
                [attr.aria-label]="i18n.t('assistant.input.send')"
                (click)="open()">
          <i-lucide name="send-horizontal" [size]="18" aria-hidden="true"></i-lucide>
          <span class="d-none d-sm-inline ms-2">{{ i18n.t('assistant.input.send') }}</span>
        </button>
      </div>
    }
  `
})
export class HomeAssistantInputComponent {
  protected readonly i18n = injectI18n(...HOME_SCOPES);
  protected readonly assistant = inject(AssistantService);
  private readonly router = inject(Router);

  public readonly draft = signal('');

  public onInput(event: Event) {
    this.draft.set((event.target as HTMLInputElement).value);
  }

  public onKeydown(event: KeyboardEvent) {
    if (event.key !== 'Enter' || event.isComposing) return;

    event.preventDefault();
    this.open();
  }

  /** В чат — с набранным: там оно уйдёт первым сообщением (2.1) */
  public open() {
    const text = this.draft().trim();
    if (text.length === 0) return;

    this.draft.set('');
    void this.router.navigate(['/assistant'], { state: { [ASSISTANT_TEXT_STATE]: text } });
  }
}
