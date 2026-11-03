import { I18nParams } from '../i18n/i18n.service';
import { serverValidationErrors, serverValidationText } from './server-validation.util';

/**
 * Текст отказа, присланный сервером.
 *
 * `GlobalExceptionHandler` кладёт в тело ответа смысловой `code` с подстановками
 * и русский `error` (`{ error, code, params, statusCode }`), а
 * `HttpErrorResponse.message` — это всегда «Http failure response for …»:
 * показывая его или свою общую фразу, интерфейс выдаёт понятный отказ
 * («Нельзя выдать права, которых нет у вас самих») за непонятную поломку.
 *
 * Порядок предпочтений — перевод по коду, потом текст сервера, потом `fallback`:
 *
 * - **перевод по коду** — единственное, что говорит на языке интерфейса;
 * - **текст сервера** — страховка на случай отставшего словаря;
 * - **`fallback`** — сервер не ответил вовсе или ответил не своим форматом:
 *   сеть, прокси, 500 без тела.
 *
 * Переводчик передаётся **параметром**, а не инжектится: утилита остаётся функцией
 * без инжектора. Ошибки 422 у поля — `server-validation.util.ts`
 * и `serverFieldErrors` ниже
 */
export interface ErrorTranslator {
  translateCode(code: string, params?: I18nParams): string | null;
}

export function serverErrorMessage(error: unknown, translator: ErrorTranslator, fallback: string): string {
  const response = error as { status?: unknown; error?: { code?: unknown; params?: I18nParams; error?: unknown } } | null;

  // Сбой сервера (5xx) не говорит человеку ничего: в теле общий текст и traceId
  // для журнала, а не объяснение — показываем свою подпись вызывающего
  if (typeof response?.status === 'number' && response.status >= 500) return fallback;

  const body = response?.error;
  const code = body?.code;

  if (typeof code === 'string' && code.length > 0) {
    const translated = translator.translateCode(code, body?.params);
    if (translated != null) return translated;
  }

  const fromBody = body?.error;

  return typeof fromBody === 'string' && fromBody.trim().length > 0 ? fromBody : fallback;
}

/**
 * Отказы 422 одного поля, переведённые по коду: ответ `{поле: [{code, params, text}]}`
 * (`server-validation.util.ts`), сервер кладёт в него коды и атрибутов DTO, и правил модели
 * (`workspace.error.emptyPermissions` у прав участника). Ключ поля сервер отдаёт в camelCase;
 * регистр здесь не важен — экраны прав называют поле как свойство DTO.
 *
 * Для формы с контролами — `applyServerValidationErrors`: ошибки встают под поля сами.
 * Здесь — для экранов, где поле не контрол формы (галки прав).
 *
 * Пусто — ответ не 422 или у поля отказов нет
 */
export function serverFieldErrors(error: unknown, field: string, translator: ErrorTranslator): string[] {
  const entry = Object.entries(serverValidationErrors(error))
    .find(([key]) => key.toLowerCase() === field.toLowerCase());
  if (!entry) return [];

  return entry[1].map(item => serverValidationText(item, (code, params) => translator.translateCode(code, params)));
}
