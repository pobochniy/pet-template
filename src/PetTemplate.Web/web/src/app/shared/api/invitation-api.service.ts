import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BaseApiService } from './base-api.service';
import { AuthenticationResultModel } from '../models/authentication-result.model';
import {
  InvitationCreateModel,
  InvitationPreviewModel,
  WorkspaceInvitationModel
} from '../models/workspace-invitation.model';

/**
 * Приглашения в пространство по ссылке (спец. authorization, 6).
 *
 * `register` и `accept` пускают пользователя внутрь: ответ — сессия, и раскладывает её
 * экран через `UserService.applyAcceptedInvitation` (принятое пространство — активным).
 * Сам сервис сессию не трогает — только запросы
 */
@Injectable({ providedIn: 'root' })
export class InvitationApiService extends BaseApiService {
  constructor(http: HttpClient) {
    super('Invitation', http);
  }

  async create(model: InvitationCreateModel): Promise<WorkspaceInvitationModel> {
    return this.post<WorkspaceInvitationModel>('Create', model);
  }

  async getList(workspaceId: string, signal?: AbortSignal): Promise<WorkspaceInvitationModel[]> {
    return (await this.get<WorkspaceInvitationModel[]>(`GetList?workspaceId=${encodeURIComponent(workspaceId)}`, signal)) ?? [];
  }

  async revoke(id: string, workspaceId: string): Promise<void> {
    await this.post<void>(`Revoke?id=${encodeURIComponent(id)}&workspaceId=${encodeURIComponent(workspaceId)}`);
  }

  /** Анонимный запрос: лендинг открывают ещё не войдя */
  async preview(token: string): Promise<InvitationPreviewModel> {
    return this.get<InvitationPreviewModel>(`Preview?token=${encodeURIComponent(token)}`);
  }

  /** `language` — язык интерфейса, как у обычной регистрации (спец. accounts, 8.4) */
  register(token: string, userName: string, password: string, passwordConfirm: string, language: string): Promise<AuthenticationResultModel> {
    return this.post<AuthenticationResultModel>('Register', {
      token,
      userName,
      password,
      passwordConfirm,
      language
    });
  }

  /** Принятие уже вошедшим: токен — в теле, query POST-запроса оседает в логах */
  accept(token: string): Promise<AuthenticationResultModel> {
    return this.post<AuthenticationResultModel>('Accept', { token });
  }

  /**
   * Полный адрес приглашения. Базовую часть собираем на клиенте — сервер знает
   * только токен, и настройки с доменом в `appsettings` из-за этого не появляется
   */
  link(token: string): string {
    return `${window.location.origin}/invite/${token}`;
  }
}
