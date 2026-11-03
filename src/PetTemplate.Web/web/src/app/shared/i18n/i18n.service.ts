import { Injectable, effect, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import { Dict, DictEntry, PluralForms } from './dict';
import { CORE, LOADERS } from './generated';
import { I18nKey, I18nPluralKey, LazyScope } from './generated/i18n-keys';
import { Lang, LANG_STORAGE_KEY, resolveInitialLang } from './lang';
import { pluralRules } from './formatters';
import { safeStorage } from '../core/safe-storage';

/**
 * Перевод интерфейса (`src/PetTemplate.Web/agents.md`, «Локализация»).
 *
 * Свой движок на сигналах вместо библиотеки: всё, что нужно, — это поиск строки
 * по ключу, подстановка и плюрализация через `Intl.PluralRules`, а сигнал даёт
 * перерисовку бесплатно. `t()` читает сигнал `dicts`, Angular видит чтение
 * в шаблонном выражении и сам перерисовывает компонент при смене языка —
 * ни пайпа, ни директивы не нужно.
 *
 * `core` обоих языков лежит в initial-бандле **статическим** импортом. Отсюда:
 * бутстрап ничего не ждёт (`provideAppInitializer` не нужен вовсе), переключение
 * языка мгновенное, а сценария «словарь не приехал» не существует. Цена —
 * ≈2–3 КБ в initial (`tech-debt/bundle.md`).
 *
 * `loadScope` — единственная точка расширения: понадобится словарь без деплоя,
 * меняется одно тело метода.
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly _lang = signal<Lang>(resolveInitialLang());

  /** Текущий язык. Читают и форматтеры, и словари, и переключатель */
  readonly lang = this._lang.asReadonly();

  /**
   * Загруженные словари по именам. Словарь экрана появляется здесь после
   * `loadScope`, `core` лежит с самого старта
   */
  private readonly dicts = signal<Readonly<Record<string, Dict>>>({ core: CORE[this._lang()] });

  /**
   * Экраны, чьи словари уже просили. Нужен для `setLang`: перезагружаем ровно те,
   * что успели загрузиться, — незагруженные приедут своим `loadScope` уже на новом языке
   */
  private readonly requested = new Set<LazyScope>();

  constructor() {
    effect(() => {
      const lang = this._lang();

      // Язык документа читают скринридер, проверка орфографии и переносы строк
      document.documentElement.lang = lang;
      document.title = this.t('core.app.title');
    });
  }

  /**
   * Строка по ключу с подстановками `{name}`.
   *
   * Ключ типизирован: опечатка — ошибка компиляции, а не молчаливое `⟦…⟧`
   * в интерфейсе. Типы генерируются из `i18n/en/*.json` вместе со словарями
   */
  t(key: I18nKey, params?: I18nParams): string {
    const entry = this.entry(key);
    if (typeof entry !== 'string') return this.missing(key);

    return interpolate(entry, params);
  }

  /**
   * Строка с числом. Категорию выбирает `Intl.PluralRules` текущего языка —
   * «1 дело / 2 дела / 5 дел» руками не собрать без таблицы правил.
   *
   * Само число доступно в строке как `{n}`
   */
  plural(key: I18nPluralKey, count: number, params?: I18nParams): string {
    const entry = this.entry(key);
    if (entry == null || typeof entry === 'string') return this.missing(key);

    const forms: PluralForms = entry;
    const category = pluralRules(this._lang()).select(count);

    return interpolate(forms[category] ?? forms.other, { n: count, ...params });
  }

  /**
   * Перевод кода, приехавшего с сервера: ключ там — обычная строка, типом
   * его не проверить. Ненайденный код — не ошибка клиента: сервер может знать
   * код, которого ещё нет в словаре, и тогда показывается присланный текст
   * (`server-error.util.ts`). Поэтому `null` вместо `⟦…⟧` и без предупреждения
   */
  translateCode(code: string, params?: I18nParams): string | null {
    const entry = this.dicts()[scopeOf(code)]?.[code];

    if (typeof entry === 'string') return interpolate(entry, params);

    // Код с числом сервер прислать может, но категорию выбирать не по чему:
    // подстановки приезжают словарём, и какая из них «количество» — неизвестно
    return null;
  }

  /**
   * Подгрузка словаря экрана. Зовётся из `loadComponent` маршрута **рядом**
   * с импортом компонента, в одном `Promise.all`: резолвер маршрута здесь
   * именно последователен — в конвейере роутера фаза `RESOLVE` идёт до
   * `LOAD COMPONENTS`, и словарь грузился бы перед чанком, а не вместе с ним
   */
  async loadScope(scope: LazyScope): Promise<void> {
    this.requested.add(scope);

    const lang = this._lang();
    if (this.dictLang.get(scope) === lang) return;

    const dict = (await LOADERS[lang][scope]()).default;

    // Язык успели переключить, пока чанк ехал: словарь на старом языке не кладём,
    // его перезагрузит `setLang` — он уже знает про этот экран
    if (this._lang() !== lang) return;

    this.dicts.update(current => ({ ...current, [scope]: dict }));
    this.dictLang.set(scope, lang);
  }

  /**
   * Смена языка. `core` меняется в тот же кадр — он в бандле; словари экранов
   * подъезжают следом и заменяются **разом**.
   *
   * Старые словари до этого момента остаются на месте намеренно: опустошив их,
   * мы показали бы человеку экран из `⟦…⟧` на время загрузки чанка
   */
  async setLang(lang: Lang): Promise<void> {
    if (lang === this._lang()) return;

    this._lang.set(lang);
    this.dicts.update(current => ({ ...current, core: CORE[lang] }));

    // Хранилище закрыто — выбор не переживёт перезагрузку, но интерфейс уже на нужном языке
    safeStorage.set(LANG_STORAGE_KEY, lang);

    const scopes = [...this.requested];

    let loaded: (readonly [LazyScope, Dict])[];
    try {
      loaded = await Promise.all(
        scopes.map(async scope => [scope, (await LOADERS[lang][scope]()).default] as const)
      );
    } catch (error) {
      // Чанк не приехал (словарь 404 после деплоя). Наружу отказ не бросаем:
      // `core` уже на новом языке, и падение здесь оборвало бы переключатель языка
      // до закрытия окна. Экраны остаются на старом словаре, а `dictLang`
      // не тронут — их перезагрузит следующий `loadScope`
      console.warn(`[i18n] screen dictionaries failed to load for "${lang}"`, error);

      return;
    }

    if (this._lang() !== lang) return;

    this.dicts.update(current => ({ ...current, ...Object.fromEntries(loaded) }));
    for (const scope of scopes) this.dictLang.set(scope, lang);
  }

  /**
   * Язык **каждого уже загруженного** словаря экрана. Одного поля на все экраны
   * мало: пока `setLang` ждёт чанки, `lang()` уже новый, а словари ещё старые —
   * общий признак в этот момент отвечал бы «загружено», и `loadScope` отдавал бы
   * словарь прошлого языка. Отдельная запись на экран переживает и отказ загрузки:
   * не обновилась — значит словарь старый и его нужно перезапросить
   */
  private readonly dictLang = new Map<LazyScope, Lang>();

  private entry(key: string): DictEntry | undefined {
    return this.dicts()[scopeOf(key)]?.[key];
  }

  /**
   * Ненайденный ключ. В разработке видно сразу и в интерфейсе, и в консоли;
   * в проде показываем последний сегмент — он читается как слово
   * («save», «Заголовок» набирать нечем) и не пугает скобками
   */
  private missing(key: string): string {
    if (!environment.production) {
      console.warn(`[i18n] missing key "${key}" in ${this._lang()}`);

      return `⟦${key}⟧`;
    }

    return key.slice(key.lastIndexOf('.') + 1);
  }
}

/**
 * Значения подстановок. `null` и `undefined` законны: в шаблон часто уходит поле
 * модели, которого может не быть (имя приглашающего у мёртвой ссылки), и `?? ''`
 * на каждом вызове был бы шумом. Пустое значение подставляется пустой строкой
 */
export type I18nParams = Readonly<Record<string, string | number | null | undefined>>;

/** Имя словаря — первый сегмент ключа */
function scopeOf(key: string): string {
  const dot = key.indexOf('.');

  return dot < 0 ? key : key.slice(0, dot);
}

/**
 * Подстановки `{name}`. Незаполненная подстановка остаётся в тексте как есть:
 * так пропуск виден, а строка не рассыпается
 */
function interpolate(text: string, params?: I18nParams): string {
  if (params == null) return text;

  return text.replace(/\{(\w+)\}/g, (placeholder, name: string) => {
    if (!Object.prototype.hasOwnProperty.call(params, name)) return placeholder;

    return params[name] == null ? '' : String(params[name]);
  });
}
