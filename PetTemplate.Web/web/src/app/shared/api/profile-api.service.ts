import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormGroup } from '@angular/forms';
import { BaseApiService } from './base-api.service';
import { TokenService } from './token.service';
import { ProfileModel } from '../models/profile.model';

@Injectable()
export class ProfileApiService extends BaseApiService {
  constructor(http: HttpClient, tokenService: TokenService) {
    super('Profile', tokenService, http);
  }

  async save(model: FormGroup) {
    return this.post('Save', model.value);
  }

  async setAvatar(avatarId: number) {
    return this.post('SetAvatar', { avatarId });
  }

  async details() {
    return this.get<ProfileModel>('Details');
  }

  async view(userId: string) {
    return this.get<ProfileModel>(`View?userId=${userId}`);
  }
}
