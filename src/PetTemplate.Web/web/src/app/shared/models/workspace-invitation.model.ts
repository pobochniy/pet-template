import type { ApiSchema } from '../api/generated/schema';

/*
 * Алиасы схем OpenAPI документа API (`npm run api:types`). Статус и права приходят
 * номерами — те же, что у `InvitationStatusEnum` и `PermissionEnum` (`enum:parity`)
 */

/**
 * Выданное приглашение — и ответ на создание, и строка в списке выданных. `token` — хвост
 * ссылки, полный адрес собирает `InvitationApiService.link()`; `acceptedByUserName` — ник
 * того, кто пришёл по ссылке, пусто, пока не пришли
 */
export type WorkspaceInvitationModel = ApiSchema<'WorkspaceInvitationDto'>;

/** Тело запроса на выдачу ссылки */
export type InvitationCreateModel = ApiSchema<'InvitationCreateDto'>;

/** Что видит на лендинге перешедший по ссылке; у мёртвой — только статус */
export type InvitationPreviewModel = ApiSchema<'InvitationPreviewDto'>;
