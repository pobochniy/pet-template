import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BaseApiService } from './base-api.service';
import { UserService } from '../core/user.service';
import { AuthenticationResultModel } from '../models/authentication-result.model';
import {
  InvitationCreateModel,
  InvitationPreviewModel,
  WorkspaceInvitationModel
} from '../models/workspace-invitation.model';

/**
 * Приглашения в пространство по ссылке (спец. authorization, 6).
 *
 * `register` и `accept` пускают пользователя внутрь, поэтому ответ раскладывает
 * тот же `UserService.applySession`, что и обычный вход, — с принятым пространством
 * активным: иначе токен и права остались бы только в возвращённом объекте
 */
@Injectable({ providedIn: 'root' })
export class InvitationApiService extends BaseApiService {
  constructor(
    http: HttpClient,
    private userService: UserService
  ) {
    super('Invitation', http);
  }

  async create(model: InvitationCreateModel): Promise<WorkspaceInvitationModel> {
    return this.post<WorkspaceInvitationModel>('Create', model);
  }

  async getList(workspaceId: string): Promise<WorkspaceInvitationModel[]> {
    return (await this.get<WorkspaceInvitationModel[]>(`GetList?workspaceId=${encodeURIComponent(workspaceId)}`)) ?? [];
  }

  async revoke(id: string, workspaceId: string): Promise<void> {
    await this.post<void>(`Revoke?id=${encodeURIComponent(id)}&workspaceId=${encodeURIComponent(workspaceId)}`);
  }

  /** Анонимный запрос: лендинг открывают ещё не войдя */
  async preview(token: string): Promise<InvitationPreviewModel> {
    return this.get<InvitationPreviewModel>(`Preview?token=${encodeURIComponent(token)}`);
  }

  /** `language` — язык интерфейса, как у обычной регистрации (спец. accounts, 8.4) */
  async register(token: string, userName: string, password: string, passwordConfirm: string, language: string): Promise<void> {
    const result = await this.post<AuthenticationResultModel>('Register', {
      token,
      userName,
      password,
      passwordConfirm,
      language
    });

    this.applyAccepted(result);
  }

  /** Принятие уже вошедшим: токен — в теле, query POST-запроса оседает в логах */
  async accept(token: string): Promise<void> {
    this.applyAccepted(await this.post<AuthenticationResultModel>('Accept', { token }));
  }

  /**
   * Полный адрес приглашения. Базовую часть собираем на клиенте — сервер знает
   * только токен, и настройки с доменом в `appsettings` из-за этого не появляется
   */
  link(token: string): string {
    return `${window.location.origin}/invite/${token}`;
  }

  /**
   * После принятия активным становится принятое пространство (спец. authorization, 6.3):
   * оно единственное, которого не было в сессии до этого
   */
  private applyAccepted(result: AuthenticationResultModel): void {
    const before = new Set(this.userService.workspaceIds());
    const accepted = Object.keys(result.permissionsByWorkspace ?? {}).find(id => !before.has(id)) ?? null;

    this.userService.applySession(result, accepted);
  }
}
