import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormGroup } from '@angular/forms';
import { BaseApiService } from './base-api.service';
import { ProfileModel } from '../models/profile.model';

@Injectable({ providedIn: 'root' })
export class ProfileApiService extends BaseApiService {
  constructor(http: HttpClient) {
    super('Profile', http);
  }

  /** Подтверждения профиля; ответ — профиль, каким он лёг в базу */
  async save(model: FormGroup) {
    return this.post<ProfileModel>('Save', model.value);
  }

  async setAvatar(avatarId: number) {
    return this.post('SetAvatar', { avatarId });
  }

  async details() {
    return this.get<ProfileModel>('Details');
  }

  async view(userId: string) {
    return this.get<ProfileModel>(`View?userId=${encodeURIComponent(userId)}`);
  }
}
