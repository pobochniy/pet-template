/**
 * Состояние приглашения — то же, что `PetTemplate.Shared.Enums.InvitationStatusEnum`
 * на сервере (спец. authorization, 6.2): номера обязаны совпадать
 */
export enum InvitationStatusEnum {
  Active = 0,
  Accepted = 1,
  Expired = 2,
  Revoked = 3
}
