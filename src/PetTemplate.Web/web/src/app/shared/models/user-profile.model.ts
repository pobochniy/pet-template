/**
 * Участник пространства в списках — `ProfileSummaryDto` на сервере. Контактов нет:
 * почта и телефон — только в своём профиле (спец. authorization, 5)
 */
export interface UserProfileModel {
  id: string;
  userName: string | null;
  avatarId: number | null;
}
