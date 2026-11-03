import type { ApiSchema } from '../api/generated/schema';

/**
 * Свой профиль — `ProfileDto` на сервере (алиас схемы OpenAPI, `npm run api:types`): ответ
 * входа, `Profile/Details` и `Profile/Save`. Сущность наружу не едет — только эти поля
 * (`ProfileMapping`).
 *
 * - `userNameIsGenerated` — логин подставлен сервером (спец. accounts, 4.2);
 * - `hasPassword` — пароль задан (8.1), сам пароль наружу не едет;
 * - `language` — язык профиля (спец. accounts, 8.4): на нём сервер пишет письма, после входа
 *   интерфейс переключается на него (`LanguageService.adopt`);
 * - `primaryWorkspaceId` — основное пространство, открывается при входе (спец. authorization,
 *   7.1). Во входе сервер присылает посчитанное: `null` — пространств нет
 */
export type ProfileModel = ApiSchema<'ProfileDto'>;
