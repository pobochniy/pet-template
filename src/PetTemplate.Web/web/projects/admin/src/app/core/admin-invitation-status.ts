/**
 * Статус приглашения админа — копия `src/PetTemplate.Shared/Enums/AdminInvitationStatus.cs`,
 * номера те же. `Expired` сервер считает по сроку, в базе его нет
 */
export enum AdminInvitationStatus {
  Pending = 0,
  Accepted = 1,
  Revoked = 2,
  Expired = 3
}

export const STATUS_TITLES: Record<AdminInvitationStatus, string> = {
  [AdminInvitationStatus.Pending]: 'ждёт',
  [AdminInvitationStatus.Accepted]: 'принято',
  [AdminInvitationStatus.Revoked]: 'отозвано',
  [AdminInvitationStatus.Expired]: 'просрочено'
};
