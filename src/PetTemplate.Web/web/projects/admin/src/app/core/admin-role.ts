/**
 * Админская роль — копия `src/PetTemplate.Shared/Enums/AdminRole.cs`, номера те же (спец. admin, 3.2).
 * Флаговая на сервере ради приглашений; здесь роли приезжают массивом, по одной.
 * Новые — только в конец, следующая — `32`
 */
export enum AdminRole {
  /** Выдача и снятие ролей, приглашения админов (3.4) */
  AdminManagement = 1,
  /** Поддержка аккаунтов: поиск, карточка, ссылка сброса, почта, удаление, журнал (спец. admin, 12) */
  Accounts = 2,
  /** Запись в «Расход»: платежи и факт поставщика, лимит ассистента у пространства (спец. admin, 14) */
  Billing = 4,
  /** Правка справочника моделей, «Замкнуть» и «Сверить» (спец. admin, 15); смотреть — любому */
  Models = 8,
  /** Разбор чужих диалогов и автооценка (спец. admin, 16) — самая тяжёлая роль: это чужая переписка */
  Dialogs = 16
}

/** Все роли по возрастанию номера — в таком порядке их и показываем */
export const ADMIN_ROLES: AdminRole[] = [
  AdminRole.AdminManagement,
  AdminRole.Accounts,
  AdminRole.Billing,
  AdminRole.Models,
  AdminRole.Dialogs
];

export const ROLE_TITLES: Record<AdminRole, string> = {
  [AdminRole.AdminManagement]: 'Управление админами',
  [AdminRole.Accounts]: 'Аккаунты',
  [AdminRole.Billing]: 'Расход',
  [AdminRole.Models]: 'Модели',
  [AdminRole.Dialogs]: 'Диалоги'
};
