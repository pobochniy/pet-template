import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, effect, inject, signal, viewChild
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountLoginsModel } from '../../shared/models/account.model';
import { UserService } from '../../shared/core/user.service';
import { WorkspaceContextService } from '../../shared/core/workspace-context.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { FormValidationComponent } from '../../shared/form-validation/form-validation.component';
import { createAccountForm, userNameValidators } from '../../shared/form-models/account-form.model';
import { EMAIL_MAX_LENGTH, USER_NAME_MAX_LENGTH } from '../../shared/form-models/account-rules';
import { applyServerValidationErrors } from '../../shared/core/server-validation.util';
import { serverErrorMessage } from '../../shared/core/server-error.util';
import { ProfilePasswordComponent } from '../password/password.component';
import { UserNameBannerComponent } from '../user-name-banner/user-name-banner.component';
import { ProfileEmailComponent } from '../email/email.component';
import { ProfileExternalLoginComponent } from '../external-login/external-login.component';
import { ProfileMergeStartComponent } from '../merge-start/merge-start.component';
import { ExternalLoginService } from '../../shared/core/external-login.service';
import { ExternalProvider } from '../../shared/models/external-login.model';
import { ProfileFreshLoginComponent } from '../fresh-login/fresh-login.component';
import { FreshLoginService } from '../fresh-login/fresh-login.service';

/**
 * Блок «Основное» своего профиля (спец. accounts, раздел 6): логин и все способы войти —
 * пароль, почта, телефон, Telegram, VK. Логин, почта, телефон и комментарий сохраняет
 * общая «Сохранить»; пароль — сам, у него свой вызов и свои отказы.
 *
 * Логин уходит своим вызовом (`Account/ChangeUserName`) и первым: у него отказ «Логин
 * занят», и показывается он под полем, а не плашкой. Не лёг логин — остальное
 * не сохраняется, чтобы человек увидел, что именно не прошло. Непоменянный логин
 * не проверяется вовсе (4.3): автоматический `tg123` правилам ввода не отвечает
 * и сохранить остальное мешать не должен.
 *
 * Почта: подтверждена ли и «Отправить ещё раз» — `profile-email` (8.4).
 *
 * Telegram и VK — `profile-external-login`: «Привязать» уводит на площадку тем же путём,
 * что вход (раздел 6); «Привязать» есть, только если площадка настроена на сервере.
 * Пароль без старого — в окне свежего входа через площадку (8.3).
 *
 * Привязка, отвязка, смена почты и слияние — в окне свежего входа (8.6): закрыто — сверху блока
 * «Подтвердите, что это вы» (`profile-fresh-login`), действие ждёт его.
 *
 * Внизу — «Объединить с другим аккаунтом» (`profile-merge-start`, раздел 7) и ссылка
 * на удаление аккаунта (`/profile/delete`, раздел 9)
 */
@Component({
  selector: 'profile-logins',
  imports: [
    ReactiveFormsModule, FormValidationComponent, ProfilePasswordComponent, UserNameBannerComponent, ProfileEmailComponent,
    ProfileExternalLoginComponent, ProfileFreshLoginComponent, ProfileMergeStartComponent, RouterLink
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './logins.component.html'
})
export class ProfileLoginsComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly workspaceContext = inject(WorkspaceContextService);

  protected readonly logins = signal<AccountLoginsModel | null>(null);

  /** Площадки, настроенные на сервере: без них «Привязать» вести некуда */
  protected readonly externalEnabled = signal<Record<ExternalProvider, boolean>>({ telegram: false, vk: false });
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);

  /** Отказ по логину, не связанный с форматом («Логин занят»), — под полем */
  protected readonly userNameRefusal = signal<string | null>(null);

  protected readonly form = createAccountForm();
  protected readonly userNameMaxLength = USER_NAME_MAX_LENGTH;
  protected readonly emailMaxLength = EMAIL_MAX_LENGTH;

  /** Что сейчас в поле «Почта» — подтверждение показывается только про сохранённый адрес */
  protected readonly emailValue = toSignal(this.form.controls.email.valueChanges, { initialValue: '' });

  protected readonly userId = computed(() => this.userService.profile()?.id ?? '');

  /**
   * Аккаунт только с паролем (раздел 6): без привязанного входа и подтверждённой почты
   * забытый пароль восстановит только поддержка
   */
  protected readonly passwordOnly = computed(() => {
    const current = this.logins();
    return !!current && current.hasPassword && !current.telegramLinked && !current.vkLinked && !current.emailConfirmed;
  });

  private readonly userNameInput = viewChild<ElementRef<HTMLInputElement>>('userNameInput');

  constructor(
    private api: AccountApiService,
    private userService: UserService,
    private alerts: AlertsService,
    private external: ExternalLoginService,
    private fresh: FreshLoginService
  ) {
    // Срок подтверждения (8.6) — из каждого ответа с «Основное», откуда бы он ни пришёл
    effect(() => {
      const logins = this.logins();
      if (logins) this.fresh.track(logins);
    });
  }

  async ngOnInit(): Promise<void> {
    this.form.controls.userName.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.userNameRefusal.set(null));

    void this.external.enabled().then(enabled => this.externalEnabled.set(enabled));
    await this.reload();
  }

  protected async reload(): Promise<void> {
    try {
      this.fill(await this.api.logins());
      this.loadFailed.set(false);
    } catch {
      this.loadFailed.set(true);
    }
  }

  protected async save(): Promise<void> {
    if (this.saving()) return;

    const userNameChanged = this.isUserNameChanged();
    const userName = this.form.controls.userName;
    userName.setValidators(userNameChanged ? userNameValidators : null);
    userName.updateValueAndValidity();

    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    // Смена почты — после подтверждения (8.6), и до логина: иначе логин лёг бы, а почта нет
    const { email, phone, comment } = this.form.getRawValue();
    const emailChanged = email.trim().toLowerCase() !== (this.logins()?.email ?? '').toLowerCase();
    if (emailChanged && !(await this.fresh.ensure())) return;

    this.saving.set(true);
    try {
      if (userNameChanged && !(await this.saveUserName())) return;

      const model = { email: email.trim() || null, phone: phone.trim() || null, comment: comment.trim() || null };
      const result = emailChanged ? await this.fresh.run(() => this.api.save(model)) : await this.api.save(model);
      if (!result) return;

      this.fill(result);
      this.syncSessionProfile(result);
      this.alerts.push('success', this.i18n.t('profile.main.saved'), 5000);
    } catch (e: unknown) {
      if ((e as { status?: number } | null)?.status === 422) {
        applyServerValidationErrors(this.form, e);
      } else {
        // Занятые почта и телефон приезжают кодами `auth.error.*` — плашкой с текстом сервера
        this.alerts.push('danger', serverErrorMessage(e, this.i18n, this.i18n.t('profile.main.saveFailed')), 10000);
      }
    } finally {
      this.saving.set(false);
    }
  }

  protected focusUserName(): void {
    const input = this.userNameInput()?.nativeElement;
    input?.scrollIntoView({ block: 'center' });
    input?.focus();
  }

  /**
   * Логин — первым. Ответ — сессия с новым логином: шапка и списки берут его из профиля
   * сессии. Отказ — под полем и `false`
   */
  private async saveUserName(): Promise<boolean> {
    try {
      const session = await this.api.changeUserName(this.form.controls.userName.value.trim());
      this.userService.applySession(session, this.workspaceContext.getCurrentWorkspaceId());
      this.logins.update(current => current && {
        ...current,
        userName: session.profile.userName ?? current.userName,
        userNameIsGenerated: false
      });
      return true;
    } catch (e: unknown) {
      if ((e as { status?: number } | null)?.status === 422) {
        applyServerValidationErrors(this.form, e);
      } else {
        this.userNameRefusal.set(serverErrorMessage(e, this.i18n, this.i18n.t('profile.main.userNameFailed')));
      }
      this.focusUserName();
      return false;
    }
  }

  /**
   * Профиль сессии лежит в `localStorage` и рисует шапку: после сохранения контакты
   * в нём — те, что легли в базу, а не те, что были при входе
   */
  private syncSessionProfile(logins: AccountLoginsModel): void {
    const profile = this.userService.profile();
    if (!profile) return;

    this.userService.setProfile({
      ...profile,
      email: logins.email ?? null,
      emailConfirmed: logins.emailConfirmed,
      phone: logins.phone ?? null,
      comment: logins.comment ?? null
    });
  }

  private isUserNameChanged(): boolean {
    return this.form.controls.userName.value.trim() !== (this.logins()?.userName ?? '');
  }

  private fill(logins: AccountLoginsModel): void {
    this.logins.set(logins);
    this.form.controls.userName.setValidators(null);
    this.form.reset({
      userName: logins.userName,
      email: logins.email ?? '',
      phone: logins.phone ?? '',
      comment: logins.comment ?? ''
    });
  }
}
