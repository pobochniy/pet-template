import { HttpClient } from "@angular/common/http";
import { Injectable } from '@angular/core';
import { UserProfileModel } from "../models/user-profile.model";
import { BaseApiService } from "./base-api.service";
import { TokenService } from "./token.service";


@Injectable()
export class UsersApiService extends BaseApiService {
  private storage?: UserProfileModel[] = undefined;

  constructor(tokenService: TokenService, http: HttpClient) {
    super('Users', tokenService, http);
  }

  public async GetProfiles() {
    if (!this.storage) {
      let apiProfiles = await this.get<any[]>('GetProfiles');
      this.storage = apiProfiles?.map(x => new UserProfileModel(x));
    }

    return this.storage;
  }

  public async getUser(userId: string) {
    const profiles = await this.GetProfiles();
    return profiles?.find(x => x.id == userId) || new UserProfileModel();
  }

  public async getUserRoles(userId: string, organizationId: string) {
    return this.get<number[]>(`GetPermissions?userId=${userId}&organizationId=${organizationId}`);
  }

  public async setUserRoles(userId: string, organizationId: string, roles: number[]) {
    return this.post('SetPermissions', { userId: userId, organizationId: organizationId, permissions: roles });
  }

  public async changeUser(model: UserProfileModel) {
    if (!this.storage) return;

    const idx = this.storage.findIndex(x => x.id == model.id);
    if (idx > -1) {
      this.storage.splice(idx, 1);
    }

    this.storage.push(model);
  }

  public async getProfilesByOrganization(organizationId: string) {
    return this.get<any[]>('GetProfilesByOrganization?organizationId=' + organizationId)
      .then(profiles => profiles?.map(x => new UserProfileModel(x)));
  }
}
