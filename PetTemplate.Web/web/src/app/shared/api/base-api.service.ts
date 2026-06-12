import { HttpClient, HttpHeaders } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { TokenService } from './token.service';

export class BaseApiService {
  private readonly baseUrl: string;

  constructor(
    public apiName: string,
    public tokenService: TokenService,
    public http: HttpClient
  ) {
    this.baseUrl = `/api/${apiName}/`;
  }

  get<T>(url: string) {
    return firstValueFrom(
      this.http.get<T>(this.baseUrl + url, {
        withCredentials: true,
        headers: new HttpHeaders({ 'Authorization': 'Bearer ' + this.tokenService.Token })
      })
    );
  }

  post<T>(url: string, data: any = {}) {
    const token = this.tokenService.Token;
    console.log('[BaseApiService] POST', this.baseUrl + url, 'Token:', token ? `${token.substring(0, 20)}...` : 'EMPTY');
    
    return firstValueFrom(
      this.http.post<T>(this.baseUrl + url, data, {
        withCredentials: true,
        headers: new HttpHeaders({ 'Authorization': 'Bearer ' + token })
      })
    );
  }

  put<T>(url: string, data: any = {}) {
    return firstValueFrom(
      this.http.put<T>(this.baseUrl + url, data, {
        withCredentials: true,
        headers: new HttpHeaders({ 'Authorization': 'Bearer ' + this.tokenService.Token })
      })
    );
  }

  delete<T>(url: string) {
    return firstValueFrom(
      this.http.delete<T>(this.baseUrl + url, {
        withCredentials: true,
        headers: new HttpHeaders({ 'Authorization': 'Bearer ' + this.tokenService.Token })
      })
    );
  }
}
