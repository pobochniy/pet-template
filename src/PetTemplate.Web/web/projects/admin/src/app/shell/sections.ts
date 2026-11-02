import { AdminRole } from '../core/admin-role';

/**
 * Разделы админки и роль, которая их открывает (спец. admin, 3.2). `null` —
 * открыто всякому, кто вошёл: сводка, состояние моделей и расход на чтение.
 * Остальные вкладки появляются вместе со своими экранами
 */
export interface Section {
  path: string;
  title: string;
  role: AdminRole | null;
}

export const SECTIONS: Section[] = [
  { path: 'summary', title: 'Сводка', role: null },
  // Смотреть — любому админу, править — роли «Модели» (спец. admin, 15)
  { path: 'models', title: 'Модели', role: null },
  { path: 'dialogs', title: 'Диалоги', role: AdminRole.Dialogs },
  { path: 'accounts', title: 'Аккаунты', role: AdminRole.Accounts },
  { path: 'account-journal', title: 'Журнал аккаунтов', role: AdminRole.Accounts },
  // Смотреть — любому админу, записывать — роли «Расход» (спец. admin, 14)
  { path: 'billing', title: 'Расход', role: null },
  { path: 'admins', title: 'Админы', role: AdminRole.AdminManagement },
  // Под AdminManagement, а не Dialogs: журнал смотрит не тот, кто его оставил (спец. admin, 16.2)
  { path: 'access-log', title: 'Журнал доступа', role: AdminRole.AdminManagement }
];
