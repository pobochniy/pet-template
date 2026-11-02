import { Injectable, inject } from '@angular/core';
import { AccountApiService } from '../api/account-api.service';
import { UserService } from '../core/user.service';
import { I18nService } from './i18n.service';
import { isLang } from './lang';

/**
 * Выбор языка: довезти выбор до профиля и принять язык профиля (спец. accounts, 8.4) —
 * на языке профиля сервер пишет письма, и с него начинает другое устройство после входа.
 *
 * Отдельно от `I18nService` намеренно (как у Lad и ASU): тот не знает ни про сеть, ни про
 * вход — его зовут из шаблонов на каждой отрисовке, и зависимость от api-сервиса втянула
 * бы `HttpClient` в цепочку, которая должна быть готова к первому кадру. Шапка грузит
 * этот сервис `import()` в момент смены языка: в initial он не едет.
 *
 * Сохранение **молчаливое**: человек уже видит нужный язык, и плашка «не удалось
 * сохранить язык» была бы сообщением ни о чём. Не прошло — письма придут на прежнем
 * языке до следующей смены; признака «не доехало», как у Lad, нет — язык не едет
 * с формой профиля, которая могла бы его довезти (как в ASU)
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly i18n = inject(I18nService);
  private readonly account = inject(AccountApiService);
  private readonly userService = inject(UserService);

  /**
   * Выбор человека — в профиль, молча; интерфейс и `localStorage` переключает
   * `I18nService.setLang` до этого. Гостю сохранять некуда: язык уедет с регистрацией,
   * а вход привезёт язык профиля
   */
  async persist(lang: string): Promise<void> {
    if (!this.userService.isAuthenticated) return;

    try {
      await this.account.setLanguage(lang);
    } catch (error) {
      console.warn('[i18n] language was not saved to the profile', error);
    }
  }

  /**
   * Язык из ответа входа: выбор, сделанный на другом устройстве, доезжает сюда.
   * Незнакомый код (сервер знает языков больше клиента) — остаёмся на текущем
   */
  async adopt(language: string | undefined | null): Promise<void> {
    if (isLang(language) && language !== this.i18n.lang()) {
      await this.i18n.setLang(language);
    }
  }
}
