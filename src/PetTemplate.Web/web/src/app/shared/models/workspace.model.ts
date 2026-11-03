import type { ApiSchema } from '../api/generated/schema';

/**
 * Пространство в списке своих — `WorkspaceDto` (алиас схемы OpenAPI, `npm run api:types`).
 * `membersCount` — сколько людей в пространстве: последнему участнику подтверждение выхода
 * говорит, что пространство удалится
 */
export type WorkspaceModel = ApiSchema<'WorkspaceDto'>;

/**
 * Ответ на создание пространства: id нового и новая сессия. Токен приезжает сразу,
 * потому что созданное пространство выдало создателю права, которых в подписанном
 * токене нет: до перевыпуска сервер отвечал бы 403 на всё закрытое правами
 * (спец. authorization, 2.2)
 */
export type WorkspaceCreatedModel = ApiSchema<'WorkspaceCreatedDto'>;
