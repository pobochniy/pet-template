import { InvitationStatusEnum } from '../shared/enums/invitation-status.enum';
import { ScopedKey } from '../shared/i18n/generated/i18n-keys';

/**
 * Ключи отказов по приглашению (спец. authorization, 6.4).
 *
 * должно быть синхронизировано с
 * PetTemplate.Shared/Constants/InvitationMessages.cs — теми же словами и под теми же
 * кодами сервер отвечает на попытку пройти по мёртвой ссылке, и расходиться им нельзя.
 *
 * Ключи, а не готовые строки: константа читается при загрузке модуля,
 * и текст застрял бы на языке, который стоял в тот момент
 */
export const INVITATION_REFUSALS: Readonly<Partial<Record<InvitationStatusEnum, ScopedKey<'invite'>>>> = {
  [InvitationStatusEnum.Expired]: 'invite.error.expired',
  [InvitationStatusEnum.Revoked]: 'invite.error.revoked',
  [InvitationStatusEnum.Accepted]: 'invite.error.accepted'
};

export function invitationRefusalKey(status: InvitationStatusEnum): ScopedKey<'invite'> {
  return INVITATION_REFUSALS[status] ?? 'invite.error.expired';
}

/**
 * Сервер не ответил — про саму ссылку это не говорит ничего, поэтому текст
 * отдельный и парной константы на сервере у него нет: просить новую ссылку
 * бессмысленно, новая вела бы туда же
 */
export const INVITATION_UNAVAILABLE_KEY: ScopedKey<'invite'> = 'invite.page.unavailable';
