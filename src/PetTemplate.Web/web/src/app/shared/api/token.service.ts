import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class TokenService {
  private _token: string = '';

  get Token(): string {
    if (!this._token) {
      this._token = localStorage.getItem('token') || '';
    }
    return this._token;
  }

  set Token(val: string) {
    this._token = val;
    localStorage.setItem('token', val);
  }
}
