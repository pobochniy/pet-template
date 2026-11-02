/**
 * Свой профиль — `ProfileDto` на сервере: ответ входа, `Profile/Details` и `Profile/Save`.
 * Сущность наружу не едет — только эти поля (`ProfileMapping`)
 */
export interface ProfileModel {
  id: string | null;
  userName: string | null;
  /** Логин подставлен сервером (спец. accounts, 4.2) */
  userNameIsGenerated?: boolean;
  /** Пароль задан (8.1) — сам пароль наружу не едет */
  hasPassword?: boolean;
  avatarId: number | null;
  email?: string | null;
  emailConfirmed?: boolean;
  phone?: string | null;
  comment: string | null;
  /**
   * Язык профиля (спец. accounts, 8.4): на нём сервер пишет письма, после входа интерфейс
   * переключается на него (`LanguageService.adopt`)
   */
  language?: string;
  isAdult: boolean;
  hasAcceptedTerms: boolean;
  /**
   * Основное пространство — открывается при входе (спец. authorization, 7.1). Во входе
   * сервер присылает посчитанное: `null` — пространств нет
   */
  primaryWorkspaceId?: string | null;
}
