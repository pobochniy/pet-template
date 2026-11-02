import { AuthenticationResultModel } from './authentication-result.model';

export interface WorkspaceModel {
  id: string;
  name: string;
  /**
   * Сколько людей в пространстве — приезжает в списке своих. Последнему участнику
   * подтверждение выхода говорит, что пространство удалится
   */
  membersCount?: number | null;
}

/**
 * Ответ на создание пространства: id нового и новая сессия. Токен приезжает сразу,
 * потому что созданное пространство выдало создателю права, которых в подписанном
 * токене нет: до перевыпуска сервер отвечал бы 403 на всё закрытое правами
 * (спец. authorization, 2.2)
 */
export interface WorkspaceCreatedModel {
  workspaceId: string;
  authentication: AuthenticationResultModel;
}
