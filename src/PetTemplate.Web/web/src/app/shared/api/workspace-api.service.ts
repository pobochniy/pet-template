import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { BaseApiService } from './base-api.service';
import { WorkspaceCreatedModel, WorkspaceModel } from '../models/workspace.model';
import { AuthenticationResultModel, PermissionsByWorkspace } from '../models/authentication-result.model';
import { WORKSPACE_HEADER } from '../interceptors/workspace.interceptor';

/** Адрес «моих прав» — его 403 перечитывание прав не запускает (`authInterceptor`) */
export const MY_PERMISSIONS_PATH = 'MyPermissions';

/**
 * Пространства пользователя (спец. authorization, 3, 7 и 8). Лежит в initial: список
 * нужен селектору в шапке
 */
@Injectable({ providedIn: 'root' })
export class WorkspaceApiService extends BaseApiService {
  constructor(http: HttpClient) {
    super('Workspace', http);
  }

  /**
   * Создание пространства. В ответе не только id, но и новая сессия: права нового
   * пространства появились в базе уже после того, как текущий токен подписан,
   * и до перевыпуска сервер считает создателя посторонним
   */
  async create(name: string): Promise<WorkspaceCreatedModel> {
    return this.post<WorkspaceCreatedModel>('Create', { name });
  }

  async getList(): Promise<WorkspaceModel[]> {
    return (await this.get<WorkspaceModel[]>('GetList')) ?? [];
  }

  /**
   * Свои права по пространствам — как они лежат в базе сейчас, без перевыпуска токена:
   * их могли поменять чужими руками (выдача и отзыв прав, удаление участника).
   * Перечитывает `PermissionsRefreshService` (спец. authorization, 2.2)
   */
  async myPermissions(signal?: AbortSignal): Promise<PermissionsByWorkspace> {
    return (await this.get<PermissionsByWorkspace>(MY_PERMISSIONS_PATH, signal)) ?? {};
  }

  /**
   * Новое название. Право проверяется в том пространстве, что в заголовке, — поэтому
   * заголовок ставим сами: строка списка может быть и не активным пространством,
   * а интерцептор поставленный заголовок не трогает
   */
  async rename(workspaceId: string, name: string): Promise<void> {
    const headers = new HttpHeaders({ [WORKSPACE_HEADER]: workspaceId });
    await firstValueFrom(this.http.post<void>(`/api/${this.apiName}/Update`, { name }, { headers }));
  }

  /** Основное пространство — свойство пользователя: менять его можно из любого своего */
  async setPrimary(workspaceId: string): Promise<void> {
    await this.post<void>(`SetPrimary?workspaceId=${encodeURIComponent(workspaceId)}`);
  }

  /**
   * Выход из пространства. В ответе новая сессия: права покинутого пространства
   * только что исчезли из базы, и без перевыпуска клиент считал бы его своим
   */
  async leave(workspaceId: string): Promise<AuthenticationResultModel> {
    return this.post<AuthenticationResultModel>(`Leave?workspaceId=${encodeURIComponent(workspaceId)}`);
  }

  /**
   * Убрать участника — отдельное явное действие: снятием всех галок человека
   * из пространства не выкидывают, сервер такой набор не примет
   */
  async removeMember(userId: string, workspaceId: string): Promise<void> {
    const query = `userId=${encodeURIComponent(userId)}&workspaceId=${encodeURIComponent(workspaceId)}`;
    await this.post<void>(`RemoveMember?${query}`);
  }
}
