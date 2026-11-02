/**
 * Отказ сервера в том виде, в каком его получает клиент: `HttpClient` кладёт тело
 * ответа в `error`, а `GlobalExceptionHandler` кладёт в тело `{ error, code, params,
 * statusCode }` — смысловой код и русскую фразу.
 *
 * Моки отказывают этим, а не голым `Error`: из тела экран достаёт текст
 * (`serverErrorMessage`), и на `Error` он показал бы запасную фразу — спека про
 * «показываем текст сервера» прошла бы, ничего не проверив. Как в ASU
 */
export function serverRefusal(status: number, message: string, code?: string, params?: Record<string, unknown>) {
  return { status, error: { error: message, code, params, statusCode: status } };
}

/**
 * Отказ валидации (422): вместо одной фразы — список кодов по полям, той же
 * формой, что у `ValidateRequestAttribute` (`{ userName: [{ code }] }`). Разбирает его
 * `applyServerValidationErrors`
 */
export function validationRefusal(errorsByField: { [field: string]: { code: string; params?: Record<string, number> }[] }) {
  return { status: 422, error: errorsByField };
}
