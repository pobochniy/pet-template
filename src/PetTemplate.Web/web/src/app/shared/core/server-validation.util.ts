import { FormGroup } from '@angular/forms';

/**
 * Ответ 422 сервера: `{ "userName": [ { "code": …, "params": …, "text": … } ] }`
 * (`ValidateRequestAttribute`, `ValidationError` на сервере). Имя поля — camelCase,
 * как оно уехало в запросе.
 *
 * Значения-объекты, а не строки (спец. accounts, 12): `min` и `max` приезжают из DTO,
 * зашить их в перевод нельзя, а строка с разделителем — неявный протокол, который
 * разъедется на первом же новом параметре.
 *
 * Сырые объекты и **хранятся** в `errors.serverError` формы. Резолвить их в строку
 * при прикреплении нельзя: уже показанные ошибки остались бы на старом языке
 * после переключения — переводит их `form-validation` при каждой отрисовке
 */

export interface ServerValidationError {
  /** Смысловой код: первый сегмент — имя словаря на клиенте */
  readonly code?: string;

  /** Подстановки к коду: `{ min: 2, max: 20 }` */
  readonly params?: Readonly<Record<string, string | number>>;

  /**
   * Текст от сервера. Есть там, где у отказа своей формулировки на клиенте
   * нет: всё, что .NET сформулировал сам и что не попало под `ValidationCodeMessages`.
   * Показываем его, когда перевода по коду нет
   */
  readonly text?: string;
}

/**
 * Разбор списка ошибок поля. Терпимый: сервер мог ответить и старым форматом
 * (массив строк), и не тем, что мы ждём, — форма от этого падать не должна
 */
export function asServerValidationErrors(value: unknown): ServerValidationError[] {
  if (!Array.isArray(value)) return [];

  return value.map(item =>
    typeof item === 'string' ? { text: item } : (item as ServerValidationError)
  );
}

/** Ошибки по полям из тела 422: `{ поле: [ошибка] }`, пустой объект на чужой формат */
export function serverValidationErrors(error: unknown): Record<string, ServerValidationError[]> {
  const response = error as { status?: number; error?: unknown } | null;
  if (response?.status !== 422 || response.error == null || typeof response.error !== 'object') {
    return {};
  }

  const result: Record<string, ServerValidationError[]> = {};
  for (const [field, value] of Object.entries(response.error as Record<string, unknown>)) {
    const errors = asServerValidationErrors(value);
    if (errors.length > 0) result[field] = errors;
  }

  return result;
}

/**
 * Раскладывает ошибки 422 по полям формы. Ошибки прикрепляются **сырыми**
 * (`{code, params, text}`): переводит их `form-validation` при каждой отрисовке.
 *
 * Имя поля сравнивается точно: сервер приводит ключ 422 к camelCase, то есть
 * к тому же имени, под которым поле уехало в запросе, — независимо от того,
 * пришёл отказ от атрибутов DTO или от разбора JSON.
 *
 * Ошибки, которым поля не нашлось (`payload` — отказ по всей форме), возвращаются:
 * их показывает экран, плашкой или строкой под формой
 */
export function applyServerValidationErrors(form: FormGroup, error: unknown): ServerValidationError[] {
  const unplaced: ServerValidationError[] = [];

  for (const [field, errors] of Object.entries(serverValidationErrors(error))) {
    const control = form.get(field);
    if (control == null) {
      unplaced.push(...errors);
      continue;
    }

    control.setErrors({ serverError: errors });
    control.markAsDirty();
  }

  return unplaced;
}

/**
 * Текст одной ошибки поля: перевод по коду, иначе присланный сервером текст,
 * иначе последний сегмент кода.
 *
 * `translate` передаётся параметром, а не инжектится: утилита остаётся функцией.
 *
 * **Единственное исключение по ключу** — `core.validation.stringLength`: границ
 * у правила может приехать как две, так и одна (`[MinLength]` и `[MaxLength]` —
 * отдельные атрибуты, каждый со своим отказом), а «От {min} до {max}» с пустой
 * подстановкой читалось бы как обрывок. Поэтому у правила три ключа, и выбирает
 * между ними набор пришедших параметров
 */
export function serverValidationText(
  error: ServerValidationError,
  translate: (code: string, params?: Readonly<Record<string, string | number>>) => string | null
): string {
  const code = error.code;
  if (code == null) return error.text ?? '';

  return translate(translationKey(code, error.params), error.params) ?? error.text ?? lastSegment(code);
}

function translationKey(code: string, params?: Readonly<Record<string, string | number>>): string {
  if (code === 'core.validation.stringLength') {
    if (params?.['min'] == null) return 'core.validation.stringLengthMax';
    if (params?.['max'] == null) return 'core.validation.stringLengthMin';
  }

  return code;
}

function lastSegment(code: string): string {
  return code.slice(code.lastIndexOf('.') + 1);
}
