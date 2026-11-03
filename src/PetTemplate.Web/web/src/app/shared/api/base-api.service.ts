import { HttpClient } from '@angular/common/http';
import { Observable, firstValueFrom } from 'rxjs';

/**
 * Основа api-сервисов: адрес `/api/{apiName}/…` и `Promise` вместо `Observable`.
 *
 * Токен в заголовок ставит `authInterceptor` — сюда он не нужен. Куки продукт
 * не использует (вход — `Bearer`, запросы того же origin), поэтому `withCredentials` нет.
 *
 * `signal` — отмена: `abort()` отписывается от `HttpClient`, и браузер обрывает
 * запрос, а промис отклоняется `AbortError`. Его передаёт `resource()` (`abortSignal`
 * загрузчика) и долгий опрос ассистента — ответ про прошлое пространство не ляжет в экран
 */
export class BaseApiService {
  private readonly baseUrl: string;

  constructor(
    public apiName: string,
    public http: HttpClient
  ) {
    this.baseUrl = `/api/${apiName}/`;
  }

  get<T>(url: string, signal?: AbortSignal) {
    return toPromise(this.http.get<T>(this.baseUrl + url), signal);
  }

  post<T>(url: string, data: unknown = {}, signal?: AbortSignal) {
    return toPromise(this.http.post<T>(this.baseUrl + url, data), signal);
  }

  put<T>(url: string, data: unknown = {}, signal?: AbortSignal) {
    return toPromise(this.http.put<T>(this.baseUrl + url, data), signal);
  }

  delete<T>(url: string, signal?: AbortSignal) {
    return toPromise(this.http.delete<T>(this.baseUrl + url), signal);
  }
}

/** Ответ запроса промисом; `signal` обрывает сам HTTP-запрос, а не только ожидание */
export function toPromise<T>(request: Observable<T>, signal?: AbortSignal): Promise<T> {
  if (!signal) return firstValueFrom(request);

  return new Promise<T>((resolve, reject) => {
    if (signal.aborted) {
      reject(abortReason(signal));
      return;
    }

    const onAbort = () => {
      subscription.unsubscribe();
      reject(abortReason(signal));
    };

    const subscription = request.subscribe({
      next: value => {
        signal.removeEventListener('abort', onAbort);
        resolve(value);
      },
      error: (error: unknown) => {
        signal.removeEventListener('abort', onAbort);
        reject(error);
      }
    });

    signal.addEventListener('abort', onAbort, { once: true });
  });
}

/** Отказ отменённого запроса — `AbortError`, как у `fetch` */
export function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}

function abortReason(signal: AbortSignal): unknown {
  return signal.reason instanceof DOMException ? signal.reason : new DOMException('Request aborted', 'AbortError');
}
