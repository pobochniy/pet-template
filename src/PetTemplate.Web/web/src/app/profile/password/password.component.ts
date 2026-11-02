import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { AccountApiService } from '../../shared/api/account-api.service';
import { UserService } from '../../shared/core/user.service';
import { WorkspaceContextService } from '../../shared/core/workspace-context.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { FormValidationComponent } from '../../shared/form-validation/form-validation.component';
import { createPasswordForm } from '../../shared/form-models/account-form.model';
import { applyServerValidationErrors } from '../../shared/core/server-validation.util';
import { serverErrorMessage } from '../../shared/core/server-error.util';

/**
 * Пароль в блоке «Основное» (спец. accounts, 6 и 8.1): «Задать пароль», если его нет,
 * «Сменить пароль» — если есть. Форма раскрывается по кнопке: пароль меняют редко,
 * и три пустых поля на экране профиля были бы шумом.
 *
 * Свежий вход через Telegram или VK (8.3) открывает «Задать новый пароль» и при заданном:
 * без старого — так восстанавливают забытый пароль через привязанный вход.
 *
 * Ответ сервера — новая сессия: отзыв завершил все прежние токены, и текущий тоже.
 * Токен из ответа уходит в `UserService.applySession` — без этого следующий запрос
 * получил бы 401 и выкинул бы человека из аккаунта. Человек тот же, поэтому это
 * не выход: `SessionResetService` не зовётся. Активное пространство сохраняется:
 * `applySession` оставляет его, раз оно среди своих
 */
@Component({
  selector: 'profile-password',
  imports: [ReactiveFormsModule, FormValidationComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './password.component.html'
})
export class ProfilePasswordComponent {
  protected readonly i18n = inject(I18nService);
  private readonly workspaceContext = inject(WorkspaceContextService);

  /** Есть ли у аккаунта пароль — от этого «задать» или «сменить» */
  readonly hasPassword = input.required<boolean>();

  /** Окно свежего входа через площадку (8.3): новый пароль задаётся без старого */
  readonly canSetWithoutCurrent = input(false);

  /** Пароль задан или сменён — родителю перечитать «Основное» */
  readonly changed = output<void>();

  protected readonly open = signal(false);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly failure = signal<string | null>(null);

  /** Со старым паролем или без — решается при раскрытии по `hasPassword` */
  protected readonly withCurrent = signal(true);
  protected readonly form = signal(createPasswordForm(true));

  constructor(private api: AccountApiService, private userService: UserService) {}

  /** `withoutCurrent` — «Задать новый пароль» в окне свежего входа (8.3) */
  protected toggle(withoutCurrent = false): void {
    const withCurrent = this.hasPassword() && !withoutCurrent;
    this.withCurrent.set(withCurrent);
    this.form.set(createPasswordForm(withCurrent));
    this.open.update(x => !x);
    this.saved.set(false);
    this.failure.set(null);
  }

  protected async submit(): Promise<void> {
    const form = this.form();
    form.markAllAsTouched();
    if (form.invalid || this.saving()) return;

    const { currentPassword, newPassword, newPasswordConfirm } = form.getRawValue();
    this.saving.set(true);
    this.failure.set(null);
    try {
      const session = this.withCurrent()
        ? await this.api.changePassword(currentPassword, newPassword, newPasswordConfirm)
        : await this.api.setPassword(newPassword, newPasswordConfirm);

      this.userService.applySession(session, this.workspaceContext.getCurrentWorkspaceId());
      this.open.set(false);
      this.saved.set(true);
      this.changed.emit();
    } catch (e: unknown) {
      if ((e as { status?: number } | null)?.status === 422) {
        applyServerValidationErrors(form, e);
      } else {
        this.failure.set(serverErrorMessage(e, this.i18n, this.i18n.t('profile.password.failed')));
      }
    } finally {
      this.saving.set(false);
    }
  }
}
