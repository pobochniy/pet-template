import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BaseApiService } from './base-api.service';
import { TokenService } from './token.service';

export interface OrganizationModel {
  id?: string;
  name?: string;
}

@Injectable()
export class OrganizationApiService extends BaseApiService {
  constructor(tokenService: TokenService, http: HttpClient) {
    super('Organization', tokenService, http);
  }

  public async getList(): Promise<OrganizationModel[] | undefined> {
    return this.get<OrganizationModel[]>('GetList');
  }
}
