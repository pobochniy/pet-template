import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { PermissionEnum } from '../shared/enums/permission.enum';
import { UserProfileModel } from '../shared/models/user-profile.model';
import { BaseApiService } from '../shared/api/base-api.service';

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
  public async getProfiles(signal?: AbortSignal): Promise<UserProfileModel[]> {
    return (await this.get<UserProfileModel[]>('GetProfiles', signal)) ?? [];
  }

  public async getUserRoles(userId: string, workspaceId: string): Promise<PermissionEnum[]> {
    const query = `userId=${encodeURIComponent(userId)}&workspaceId=${encodeURIComponent(workspaceId)}`;

    return (await this.get<PermissionEnum[]>(`GetPermissions?${query}`)) ?? [];
  }

  public async setUserRoles(userId: string, workspaceId: string, roles: PermissionEnum[]): Promise<void> {
    await this.post('SetPermissions', { userId, workspaceId, permissions: roles });
  }
}
