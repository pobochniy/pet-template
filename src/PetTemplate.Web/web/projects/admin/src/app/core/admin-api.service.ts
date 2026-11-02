import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  Admin,
  AdminAccessLogFilter,
  AdminAccessLogPage,
  AdminDialogFilter,
  AdminDialogPage,
  AdminDialogTrace,
  AdminEvaluationStart,
  AdminEvaluations,
  AdminJob,
  AdminModelEdit,
  AdminModels,
  AdminAccount,
  AdminAccountCard,
  AdminAccountJournalFilter,
  AdminAccountJournalPage,
  AdminAssistantLimit,
  AdminBillingSummary,
  AdminBillingWorkspaceCard,
  AdminInvitation,
  AdminInvitationPreview,
  AdminIssuedReset,
  AdminLedgerEntry,
  AdminLoginResult,
  AdminMember,
  AdminReleaseEmail,
  AdminReleasePhone,
  AdminReleasedContact,
  AdminSummary,
  IssuedAdminInvitation
} from './admin.model';
import { AdminRole } from './admin-role';

/** Признаки страницы — в параметры запроса; пустые не уходят, курсор — `before` */
function query(filter: object, before?: string): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(filter)) {
    if (value !== null && value !== undefined && value !== '') params = params.set(key, String(value));
  }
  return before ? params.set('before', before) : params;
}

/** Вызовы `api/Admin/...` (спец. admin, 6). Новый раздел добавляет сюда свои */
@Injectable({ providedIn: 'root' })
export class AdminApiService {
  private readonly http = inject(HttpClient);

  /** Логин и пароль — от продукта; своей учётки у админки нет (спец. admin, 3.1) */
  login(login: string, password: string): Promise<AdminLoginResult> {
    return firstValueFrom(this.http.post<AdminLoginResult>('/api/Admin/Login', { login, password }));
  }

  /**
   * В каркасе серверу гасить нечего — пустой `200`. Метод есть, чтобы со шлюзом
   * в него встало гашение куки, не меняя клиента (спец. admin, 3.3 и 6)
   */
  logout(): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/Admin/Logout', null));
  }

  me(): Promise<Admin> {
    return firstValueFrom(this.http.get<Admin>('/api/Admin/Me'));
  }

  summary(): Promise<AdminSummary> {
    return firstValueFrom(this.http.get<AdminSummary>('/api/Admin/Summary'));
  }

  /** Полный реиндекс поиска — в очередь (спец. search, 5) */
  reindexSearch(): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/Admin/Search/Reindex', null));
  }

  /** Справочник моделей (спец. admin, 15.1) — смотреть любому админу */
  models(): Promise<AdminModels> {
    return firstValueFrom(this.http.get<AdminModels>('/api/Admin/Models'));
  }

  /** Новая модель встаёт последней; отвечает её идентификатором */
  createModel(model: AdminModelEdit): Promise<string> {
    return firstValueFrom(this.http.post<string>('/api/Admin/Models', model));
  }

  /** Правка с `version` строки: её поменяли за это время — отказ «откройте заново» (15.1) */
  updateModel(id: string, model: AdminModelEdit): Promise<void> {
    return firstValueFrom(this.http.put<void>(`/api/Admin/Models/${id}`, model));
  }

  /** Порядок целиком: все модели справочника в новом порядке (спец. admin, 15.2) */
  reorderModels(ids: string[]): Promise<void> {
    return firstValueFrom(this.http.put<void>('/api/Admin/Models/Order', ids));
  }

  /** Замкнуть руками — отменить паузу размыкателя, когда модель ожила (спец. assistant, 16.2) */
  closeModel(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/Admin/Models/${id}/Close`, null));
  }

  /** Сверить модель набором фраз — в том числе выключенную (спец. admin, 15.3). Идёт фоном */
  checkModel(id: string): Promise<AdminJob> {
    return firstValueFrom(this.http.post<AdminJob>(`/api/Admin/Models/${id}/Check`, null));
  }

  /** Последние сверки — без отчётов */
  modelChecks(): Promise<AdminJob[]> {
    return firstValueFrom(this.http.get<AdminJob[]>('/api/Admin/Models/Checks'));
  }

  /** Сверка с отчётом на сейчас */
  modelCheck(jobId: string): Promise<AdminJob> {
    return firstValueFrom(this.http.get<AdminJob>(`/api/Admin/Models/Checks/${jobId}`));
  }

  /** Список прогонов по признакам; `before` — последний прогон прошлой страницы (спец. admin, 16.2) */
  dialogs(filter: AdminDialogFilter, before?: string): Promise<AdminDialogPage> {
    return firstValueFrom(this.http.get<AdminDialogPage>('/api/Admin/Dialogs', { params: query(filter, before) }));
  }

  /** Трасса прогона. Каждый вызов пишется в журнал доступа (спец. admin, 16.2) */
  dialog(runId: string): Promise<AdminDialogTrace> {
    return firstValueFrom(this.http.get<AdminDialogTrace>(`/api/Admin/Dialogs/${runId}`));
  }

  /** Потолок пачки и последние запуски автооценки (спец. admin, 16.3) */
  evaluations(): Promise<AdminEvaluations> {
    return firstValueFrom(this.http.get<AdminEvaluations>('/api/Admin/Dialogs/Evaluations'));
  }

  /** «Оценить»: выбранный судья и размер пачки. Отвечает запуском */
  startEvaluation(start: AdminEvaluationStart): Promise<AdminJob> {
    return firstValueFrom(this.http.post<AdminJob>('/api/Admin/Dialogs/Evaluations', start));
  }

  /** Кто читал чужую переписку; `before` — последняя запись прошлой страницы (спец. admin, 16.2) */
  accessLog(filter: AdminAccessLogFilter, before?: string): Promise<AdminAccessLogPage> {
    return firstValueFrom(this.http.get<AdminAccessLogPage>('/api/Admin/AccessLog', { params: query(filter, before) }));
  }

  /** Аккаунты по логину, почте, телефону, Telegram или VK (спец. admin, 12) */
  searchAccounts(text: string): Promise<AdminAccount[]> {
    return firstValueFrom(this.http.get<AdminAccount[]>('/api/Admin/Accounts', { params: { query: text } }));
  }

  accountCard(id: string): Promise<AdminAccountCard> {
    return firstValueFrom(this.http.get<AdminAccountCard>(`/api/Admin/Accounts/${id}`));
  }

  /** Журнал аккаунтов: действия админов и слияния, новые сверху, страницами */
  accountJournal(filter: AdminAccountJournalFilter, before?: string): Promise<AdminAccountJournalPage> {
    return firstValueFrom(
      this.http.get<AdminAccountJournalPage>('/api/Admin/Accounts/Journal', { params: query(filter, before) })
    );
  }

  /** Удалить аккаунт без пространств — с причиной в журнале */
  deleteAccount(id: string, reason: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/Admin/Accounts/${id}/Delete`, { reason }));
  }

  /** Ссылка сброса пароля — с причиной в журнале; ссылку админ отправляет сам */
  issueReset(id: string, reason: string): Promise<AdminIssuedReset> {
    return firstValueFrom(this.http.post<AdminIssuedReset>(`/api/Admin/Accounts/${id}/IssueReset`, { reason }));
  }

  /** Снять неподтверждённую почту с чужого аккаунта для аккаунта `id` */
  releaseEmail(id: string, release: AdminReleaseEmail): Promise<AdminReleasedContact> {
    return firstValueFrom(this.http.post<AdminReleasedContact>(`/api/Admin/Accounts/${id}/ReleaseEmail`, release));
  }

  /** Снять телефон с чужого аккаунта для аккаунта `id` */
  releasePhone(id: string, release: AdminReleasePhone): Promise<AdminReleasedContact> {
    return firstValueFrom(this.http.post<AdminReleasedContact>(`/api/Admin/Accounts/${id}/ReleasePhone`, release));
  }

  /** «Расход» (спец. admin, 14): дни по UTC `yyyy-MM-dd`, оба конца включительно */
  billingSummary(from: string, to: string, modelId: string | null): Promise<AdminBillingSummary> {
    let params = new HttpParams().set('from', from).set('to', to);
    if (modelId) params = params.set('modelId', modelId);
    return firstValueFrom(this.http.get<AdminBillingSummary>('/api/Admin/Billing/Summary', { params }));
  }

  /** Лимит пространства и кто сколько потратил за период */
  billingWorkspace(id: string, from: string, to: string): Promise<AdminBillingWorkspaceCard> {
    const params = new HttpParams().set('from', from).set('to', to);
    return firstValueFrom(this.http.get<AdminBillingWorkspaceCard>(`/api/Admin/Billing/Workspaces/${id}`, { params }));
  }

  /** Платёж поставщику или факт расхода; правка факта — тем же периодом */
  recordLedger(entry: AdminLedgerEntry): Promise<string> {
    return firstValueFrom(this.http.post<string>('/api/Admin/Billing/Ledger', entry));
  }

  /** Свой лимит пространства целиком; `null` в поле — без потолка */
  setWorkspaceLimit(id: string, limit: AdminAssistantLimit): Promise<void> {
    return firstValueFrom(this.http.put<void>(`/api/Admin/Billing/Workspaces/${id}/Limit`, limit));
  }

  /** Вернуть пространство к лимиту по умолчанию */
  resetWorkspaceLimit(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/Admin/Billing/Workspaces/${id}/Limit`));
  }

  admins(): Promise<AdminMember[]> {
    return firstValueFrom(this.http.get<AdminMember[]>('/api/Admin/Admins'));
  }

  /** Ещё одна роль тому, кто уже админ; не больше своих (спец. admin, 3.4) */
  grantRole(userId: string, role: AdminRole): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/Admin/Admins/${userId}/Roles/${role}`, null));
  }

  /** Последнюю роль так не снять — только `removeAdmin` */
  revokeRole(userId: string, role: AdminRole): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/Admin/Admins/${userId}/Roles/${role}`));
  }

  removeAdmin(userId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/Admin/Admins/${userId}`));
  }

  invitations(): Promise<AdminInvitation[]> {
    return firstValueFrom(this.http.get<AdminInvitation[]>('/api/Admin/Invitations'));
  }

  createInvitation(roles: AdminRole[]): Promise<IssuedAdminInvitation> {
    return firstValueFrom(this.http.post<IssuedAdminInvitation>('/api/Admin/Invitations', { roles }));
  }

  revokeInvitation(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/Admin/Invitations/${id}`));
  }

  /** Страница приглашения до входа; незнакомый токен — `404` */
  invitationPreview(token: string): Promise<AdminInvitationPreview> {
    return firstValueFrom(this.http.get<AdminInvitationPreview>(`/api/Admin/Invitations/${token}`));
  }

  /** Принять логином и паролем от продукта — в ответ сразу админская сессия */
  acceptInvitation(token: string, login: string, password: string): Promise<AdminLoginResult> {
    return firstValueFrom(
      this.http.post<AdminLoginResult>(`/api/Admin/Invitations/${token}/Accept`, { login, password })
    );
  }
}
