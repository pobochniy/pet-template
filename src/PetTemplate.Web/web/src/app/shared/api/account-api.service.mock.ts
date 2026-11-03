import { Injectable } from '@angular/core';
import { FormControl } from '@angular/forms';
import { IAccountApiService } from './account-api.service';
import {
  AccountCardModel, AccountLoginsModel, AccountSaveModel, LinkResultModel, MergePreviewModel, MergeProofModel
} from '../models/account.model';
import { AuthenticationResultModel } from '../models/authentication-result.model';
import { ExternalProofModel } from '../models/external-login.model';
import { ExternalProviderEnum } from '../enums/external-provider.enum';
import { serverRefusal, validationRefusal } from './server-refusal.mock';
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH, phoneValidator, userNameValidator } from '../form-models/account-rules';
import { profileDto, sessionDto } from './dto.mock';

/**
 * Подмена `AccountApiService` для спек. Повторяет правила сервера (`AccountService`,
 * `AccountController`): занятый логин — без учёта регистра и `400` с кодом; формат
 * и резерв логина, телефон и длина пароля — `422` по полю; неверный текущий пароль —
 * `422` по `currentPassword`; «задать» при заданном пароле — `400`; занятые почта
 * и телефон — `400`; смена почты сбрасывает подтверждение; «Отправить ещё раз» без почты
 * и сверх лимита — `400`; в окне свежего входа (`canSetPasswordWithoutCurrent`) «задать»
 * проходит и поверх пароля (8.3); привязка второго входа площадки — `400`, отвязка
 * последнего способа войти — `400` (раздел 3); привязка, отвязка, смена почты и просмотр
 * слияния — только до `freshLoginUntilUtc` (8.6), подтверждение паролем продлевает его
 * на 10 минут; вход чужого аккаунта при привязке — просмотр слияния (7.1); подтверждение
 * слияния после перемены состава — `409`; подтверждение удаления — свой логин без учёта регистра.
 *
 * `hold()` придерживает следующий ответ до `release()` — так спека проверяет, что экран
 * перерисовывается от сигнала, а не от самого клика
 */
@Injectable()
export class AccountApiServiceMock implements IAccountApiService {
  account: AccountLoginsModel = {
    userName: 'tester',
    userNameIsGenerated: false,
    hasPassword: true,
    email: 'tester@example.com',
    emailConfirmed: true,
    phone: null,
    comment: null,
    telegramLinked: false,
    vkLinked: false,
    loginCount: 1,
    canSetPasswordWithoutCurrent: false,
    // Подтверждено надолго: спекам, которые проверяют не подтверждение, оно не мешает
    freshLoginUntilUtc: '2999-01-01T00:00:00Z'
  };

  /** Текущий пароль аккаунта */
  password = 'Staryi-parol-42';

  /** Логины, почты и телефоны других аккаунтов */
  takenUserNames: string[] = ['boris'];
  takenEmails: string[] = ['boris@example.com'];
  takenPhones: string[] = ['+79990000002'];

  /** Сколько сессий выдано: каждая смена пароля и логина отвечает новой */
  issuedSessions = 0;

  private gate: Promise<void> | null = null;
  private open: (() => void) | null = null;

  hold(): void {
    this.gate = new Promise(resolve => (this.open = resolve));
  }

  release(): void {
    this.open?.();
    this.gate = null;
    this.open = null;
  }

  async logins(): Promise<AccountLoginsModel> {
    await this.wait();
    return { ...this.account };
  }

  async save(model: AccountSaveModel): Promise<AccountLoginsModel> {
    await this.wait();

    const email = model.email?.trim() || null;
    const phone = model.phone?.trim() || null;
    if (phone && phoneValidator(new FormControl(phone))) {
      throw validationRefusal({ phone: [{ code: 'core.validation.phoneFormat' }] });
    }
    if (email && this.takenEmails.includes(email.toLowerCase())) {
      throw serverRefusal(400, 'Эта почта уже зарегистрирована', 'auth.error.emailTaken');
    }
    if (phone && this.takenPhones.includes(phone)) {
      throw serverRefusal(400, 'Этот телефон уже зарегистрирован', 'auth.error.phoneTaken');
    }

    const emailChanged = (this.account.email ?? '').toLowerCase() !== (email ?? '').toLowerCase();
    if (emailChanged) this.requireFresh();
    this.account = {
      ...this.account,
      email,
      phone,
      comment: model.comment || null,
      emailConfirmed: emailChanged ? false : this.account.emailConfirmed
    };

    return { ...this.account };
  }

  async changeUserName(userName: string): Promise<AuthenticationResultModel> {
    await this.wait();

    const value = userName.trim();
    const errors = userNameValidator(new FormControl(value));
    if (errors) {
      const code = errors['userNameReserved'] ? 'core.validation.userNameReserved' : 'core.validation.userNameFormat';
      throw validationRefusal({ userName: [{ code }] });
    }
    if (this.takenUserNames.includes(value.toLowerCase())) {
      throw serverRefusal(400, 'Такое имя пользователя уже занято', 'auth.error.userNameTaken');
    }

    this.account = { ...this.account, userName: value, userNameIsGenerated: false };
    return this.session();
  }

  async setPassword(newPassword: string, newPasswordConfirm: string): Promise<AuthenticationResultModel> {
    await this.wait();

    if (this.account.hasPassword && !this.account.canSetPasswordWithoutCurrent) {
      throw serverRefusal(400, 'Пароль уже задан — смените его, указав текущий', 'auth.error.passwordAlreadySet');
    }

    return this.replacePassword(newPassword, newPasswordConfirm);
  }

  async changePassword(currentPassword: string, newPassword: string, newPasswordConfirm: string): Promise<AuthenticationResultModel> {
    await this.wait();

    if (currentPassword !== this.password) {
      throw validationRefusal({ currentPassword: [{ code: 'auth.error.wrongPassword' }] });
    }

    return this.replacePassword(newPassword, newPasswordConfirm);
  }

  async confirmPassword(password: string): Promise<AuthenticationResultModel> {
    await this.wait();

    if (!this.account.hasPassword || password !== this.password) {
      throw validationRefusal({ password: [{ code: 'auth.error.wrongPassword' }] });
    }

    this.account = { ...this.account, freshLoginUntilUtc: new Date(Date.now() + 10 * 60_000).toISOString() };
    return this.session();
  }

  /** Писем подтверждения, отправленных «Отправить ещё раз» */
  confirmationsSent = 0;

  /** Следующее «Отправить ещё раз» упрётся в лимит (8.4) */
  mailLimitReached = false;

  async sendEmailConfirmation(): Promise<unknown> {
    await this.wait();

    if (!this.account.email) {
      throw serverRefusal(400, 'Сначала укажите почту', 'auth.error.noEmail');
    }
    if (this.mailLimitReached) {
      throw serverRefusal(400, 'Письмо уже отправлено — подождите пару минут', 'auth.error.tooManyEmails');
    }

    this.confirmationsSent++;
    return {};
  }

  /** Язык, сохранённый переключателем */
  language: string | null = null;

  async setLanguage(language: string): Promise<unknown> {
    await this.wait();
    if (!['ru', 'en'].includes(language)) {
      throw validationRefusal({ language: [{ code: 'core.validation.language' }] });
    }
    this.language = language;
    return {};
  }

  /** Последнее доказательство, ушедшее в `Account/Link` */
  linkedProof: ExternalProofModel | null = null;

  /** Площадки, чей вход уже принадлежит другому аккаунту: привязка отвечает просмотром слияния */
  takenExternal = new Set<ExternalProviderEnum>();

  async link(proof: ExternalProofModel): Promise<LinkResultModel> {
    await this.wait();
    this.requireFresh();
    this.linkedProof = proof;

    // Вход чужого аккаунта — привязки нет, сразу просмотр слияния (7.1)
    if (this.takenExternal.has(proof.provider)) {
      return { logins: null, mergePreview: this.preview };
    }

    const telegram = proof.provider === ExternalProviderEnum.Telegram;
    if (telegram ? this.account.telegramLinked : this.account.vkLinked) {
      throw serverRefusal(400, 'К аккаунту уже привязан другой вход этой площадки', 'auth.error.loginAlreadyLinked');
    }

    this.account = {
      ...this.account,
      telegramLinked: this.account.telegramLinked || telegram,
      vkLinked: this.account.vkLinked || !telegram,
      loginCount: this.account.loginCount + 1
    };
    return { logins: { ...this.account }, mergePreview: null };
  }

  async unlink(provider: ExternalProviderEnum): Promise<AccountLoginsModel> {
    await this.wait();
    this.requireFresh();

    const telegram = provider === ExternalProviderEnum.Telegram;
    const linked = telegram ? this.account.telegramLinked : this.account.vkLinked;
    if (!linked) return { ...this.account };
    if (this.account.loginCount <= 1) {
      throw serverRefusal(400, 'Это последний способ войти', 'auth.error.lastLogin');
    }

    this.account = {
      ...this.account,
      telegramLinked: telegram ? false : this.account.telegramLinked,
      vkLinked: telegram ? this.account.vkLinked : false,
      loginCount: this.account.loginCount - 1
    };
    return { ...this.account };
  }

  /** Карточка своего аккаунта: «Рога и Копыта» с Борисом и «Моё пространство» — только он */
  card: AccountCardModel = accountCard('user-1', 'tester', [
    { id: 'ws-1', name: 'Рога и Копыта', isOwner: false, otherMembers: ['boris'], isSolo: false },
    { id: 'ws-2', name: 'Моё пространство', isOwner: true, otherMembers: [], isSolo: true }
  ]);

  /** Второй аккаунт, чьё владение «доказано»: пароль `Vtoroi-parol-42` или вход площадки */
  other: AccountCardModel = {
    ...accountCard('user-2', 'tg123456789', [
      { id: 'ws-3', name: 'Шарики и Палочки', isOwner: true, otherMembers: [], isSolo: true },
      { id: 'ws-1', name: 'Рога и Копыта', isOwner: true, otherMembers: ['boris', 'tester'], isSolo: false }
    ]),
    hasPassword: false,
    hasTelegram: true
  };

  /** Второй заведён не меньше суток назад — его можно оставить (7.3) */
  canKeepOther = true;

  get preview(): MergePreviewModel {
    return {
      ticket: 'ticket-1',
      expiresAtUtc: '2026-10-25T10:10:00Z',
      current: this.card,
      other: this.other,
      canKeepOther: this.canKeepOther
    };
  }

  /** Коды площадки, которые «подтвердят» владение вторым аккаунтом */
  validProofCodes = new Set<string>(['good-code']);

  /** Следующее подтверждение слияния ответит `409`: состав пространств поменялся */
  mergeConflict = false;

  /** С каким ключом и кого оставили */
  merged: { ticket: string; keepUserId: string } | null = null;

  /** Логин, которым подтвердили удаление */
  deletedWith: string | null = null;

  async mergePreview(proof: MergeProofModel): Promise<MergePreviewModel> {
    await this.wait();
    this.requireFresh();

    if (proof.external) {
      if (!proof.external.code || !this.validProofCodes.has(proof.external.code)) {
        throw serverRefusal(400, 'Не удалось войти через Telegram или VK. Попробуйте ещё раз', 'auth.error.externalFailed');
      }
      return this.preview;
    }
    if (proof.login?.toLowerCase() === this.account.userName.toLowerCase()) {
      throw serverRefusal(400, 'Это тот же аккаунт, в который вы вошли', 'auth.error.mergeSameAccount');
    }
    if (proof.login !== this.other.userName || proof.password !== 'Vtoroi-parol-42') {
      throw validationRefusal({ payload: [{ code: 'auth.error.invalidCredentials' }] });
    }

    return this.preview;
  }

  async merge(ticket: string, keepUserId: string): Promise<AuthenticationResultModel> {
    await this.wait();

    if (this.mergeConflict) {
      throw serverRefusal(409, 'С тех пор поменялся состав пространств — посмотрите слияние заново', 'auth.error.mergeChanged');
    }
    if (keepUserId === this.other.userId && !this.canKeepOther) {
      throw serverRefusal(400, 'Второй аккаунт заведён меньше суток назад — оставить можно только текущий',
        'auth.error.mergeKeepYoungAccount');
    }

    this.merged = { ticket, keepUserId };
    return this.session();
  }

  async deletePreview(): Promise<AccountCardModel> {
    await this.wait();
    return this.card;
  }

  async deleteAccount(userName: string): Promise<unknown> {
    await this.wait();

    if (userName.trim().toLowerCase() !== this.account.userName.toLowerCase()) {
      throw serverRefusal(400, 'Введите свой логин, чтобы подтвердить удаление', 'auth.error.deleteConfirmMismatch');
    }

    this.deletedWith = userName;
    return {};
  }

  private replacePassword(newPassword: string, newPasswordConfirm: string): AuthenticationResultModel {
    if (newPassword.length < PASSWORD_MIN_LENGTH || newPassword.length > PASSWORD_MAX_LENGTH) {
      throw validationRefusal({
        newPassword: [{ code: 'core.validation.stringLength', params: { min: PASSWORD_MIN_LENGTH, max: PASSWORD_MAX_LENGTH } }]
      });
    }
    if (newPassword !== newPasswordConfirm) {
      throw validationRefusal({ newPasswordConfirm: [{ code: 'core.validation.mismatch' }] });
    }

    this.password = newPassword;
    const added = this.account.hasPassword ? 0 : 1;
    this.account = { ...this.account, hasPassword: true, loginCount: this.account.loginCount + added };
    return this.session();
  }

  /** Сессия так, как её отдаёт `AuthResultFactory`: токен, профиль, права */
  private session(): AuthenticationResultModel {
    this.issuedSessions++;

    return sessionDto({
      token: `token-${this.issuedSessions}`,
      profile: profileDto({
        id: 'user-1',
        userName: this.account.userName,
        userNameIsGenerated: this.account.userNameIsGenerated,
        hasPassword: this.account.hasPassword,
        avatarId: 0,
        email: this.account.email,
        emailConfirmed: this.account.emailConfirmed,
        phone: this.account.phone,
        comment: this.account.comment ?? null,
        isAdult: false,
        hasAcceptedTerms: false
      }),
      permissionsByWorkspace: {}
    });
  }

  private requireFresh(): void {
    const until = this.account.freshLoginUntilUtc;
    if (!until || Date.parse(until) <= Date.now()) {
      throw serverRefusal(400, 'Подтвердите, что это вы: введите пароль или войдите заново через Telegram или VK',
        'auth.error.freshLoginRequired');
    }
  }

  private async wait(): Promise<void> {
    if (this.gate) await this.gate;
  }
}

function accountCard(userId: string, userName: string, workspaces: AccountCardModel['workspaces']): AccountCardModel {
  return {
    userId,
    userName,
    registeredAtUtc: '2025-03-01T00:00:00Z',
    hasPassword: true,
    hasTelegram: false,
    hasVk: false,
    canBeRemoved: true,
    activity: {},
    workspaces
  };
}
