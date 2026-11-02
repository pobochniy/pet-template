import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { PermissionEnum } from '../enums/permission.enum';
import { UserProfileModel } from '../models/user-profile.model';
import { BaseApiService } from './base-api.service';

/**
 * Состав пространства и права участников (спец. authorization, 5). Пространство
 * запроса — активное, из заголовка `X-Workspace-Id` (`workspaceInterceptor`);
 * `workspaceId` в параметрах сервер сверяет с ним
 */
@Injectable({ providedIn: 'root' })
export class UsersApiService extends BaseApiService {
  constructor(http: HttpClient) {
    super('Users', http);
  }

  /** Участники активного пространства — без контактов */
  public async getProfiles(): Promise<UserProfileModel[]> {
    return (await this.get<UserProfileModel[]>('GetProfiles')) ?? [];
  }

  public async getUserRoles(userId: string, workspaceId: string): Promise<PermissionEnum[]> {
    const query = `userId=${encodeURIComponent(userId)}&workspaceId=${encodeURIComponent(workspaceId)}`;

    return (await this.get<PermissionEnum[]>(`GetPermissions?${query}`)) ?? [];
  }

  public async setUserRoles(userId: string, workspaceId: string, roles: PermissionEnum[]): Promise<void> {
    await this.post('SetPermissions', { userId, workspaceId, permissions: roles });
  }
}
