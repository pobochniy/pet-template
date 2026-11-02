import { InvitationStatusEnum } from '../enums/invitation-status.enum';
import { PermissionEnum } from '../enums/permission.enum';

/** Выданное приглашение — и ответ на создание, и строка в списке выданных */
export interface WorkspaceInvitationModel {
  id: string;
  /** Хвост ссылки; полный адрес собирает `InvitationApiService.link()` */
  token: string;
  inviteeName: string | null;
  status: InvitationStatusEnum;
  createdAtUtc: string;
  expiresAtUtc: string;
  /** Ник того, кто пришёл по ссылке; пусто, пока не пришли */
  acceptedByUserName: string | null;
  permissions: PermissionEnum[];
}

/** Тело запроса на выдачу ссылки */
export interface InvitationCreateModel {
  workspaceId: string;
  inviteeName: string | null;
  permissions: PermissionEnum[];
}

/** Что видит на лендинге перешедший по ссылке; у мёртвой — только статус */
export interface InvitationPreviewModel {
  status: InvitationStatusEnum;
  workspaceName: string | null;
  inviterName: string | null;
  inviteeName: string | null;
}
