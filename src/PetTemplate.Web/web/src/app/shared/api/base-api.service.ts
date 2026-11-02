import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/** Токен в заголовок ставит `authInterceptor` — сюда он не нужен */
export class BaseApiService {
  private readonly baseUrl: string;

  constructor(
    public apiName: string,
    public http: HttpClient
  ) {
    this.baseUrl = `/api/${apiName}/`;
  }

  get<T>(url: string) {
    return firstValueFrom(this.http.get<T>(this.baseUrl + url, { withCredentials: true }));
  }

  post<T>(url: string, data: unknown = {}) {
    return firstValueFrom(this.http.post<T>(this.baseUrl + url, data, { withCredentials: true }));
  }

  put<T>(url: string, data: unknown = {}) {
    return firstValueFrom(this.http.put<T>(this.baseUrl + url, data, { withCredentials: true }));
  }

  delete<T>(url: string) {
    return firstValueFrom(this.http.delete<T>(this.baseUrl + url, { withCredentials: true }));
  }
}
