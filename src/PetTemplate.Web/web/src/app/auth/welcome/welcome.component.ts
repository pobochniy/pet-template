import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AccountApiService } from '../../shared/api/account-api.service';
import { UserService } from '../../shared/core/user.service';
import { WorkspaceContextService } from '../../shared/core/workspace-context.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { FormValidationComponent } from '../../shared/form-validation/form-validation.component';
import { createPasswordForm, userNameValidators } from '../../shared/form-models/account-form.model';
import { USER_NAME_MAX_LENGTH } from '../../shared/form-models/account-rules';
import { applyServerValidationErrors } from '../../shared/core/server-validation.util';
import { serverErrorMessage } from '../../shared/core/server-error.util';

/**
 * «Добро пожаловать» (`/welcome`, спец. accounts, 5.4) — то, что спросила бы форма
 * регистрации, для аккаунта, который площадка завела без вопросов: логин (уже заполнен
 * автоматическим, 4.2) и необязательный пароль дважды — «задайте, чтобы входить и по логину».
 *
 * Открывается один раз — сразу после заведения аккаунта через Telegram или VK, — и
 * правилом-охранником не держится: закрыл вкладку — дальше главная, где человек без
 * пространства видит предложение его создать (спец. authorization, 3).
 *
 * Сохраняет теми же вызовами, что профиль, по очереди: `Account/ChangeUserName`
 * (если поменяли), потом `Account/SetPassword` (если ввели). Оба отвечают новой сессией.
 * Сбой посередине не страшен: повторное нажатие доделывает остальное — сохранённый
 * логин уже совпадает с полем, заданный пароль второй раз не задаётся
 */
@Component({
  selector: 'auth-welcome',
  imports: [ReactiveFormsModule, FormValidationComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './welcome.component.html',
  styles: ['.auth-welcome { max-width: 560px; }']
})
export class WelcomeComponent {
  protected readonly i18n = inject(I18nService);
  /** Полем, а не параметром конструктора: им заполняется поле логина ниже */
  private readonly userService = inject(UserService);
  private readonly workspaceContext = inject(WorkspaceContextService);

  protected readonly userNameMaxLength = USER_NAME_MAX_LENGTH;
  protected readonly userName = new FormControl(this.savedUserName(), { nonNullable: true });
  protected readonly passwordForm = createPasswordForm(false);
  protected readonly form = new FormGroup({ userName: this.userName });

  /** Пароль уже задан этим экраном — повторное нажатие после сбоя его не трогает */
  protected readonly passwordSet = signal(false);
  protected readonly busy = signal(false);

  /** Отказ по логину, не связанный с форматом («Логин занят»), — под полем */
  protected readonly userNameRefusal = signal<string | null>(null);

  constructor(
    private account: AccountApiService,
    private alerts: AlertsService,
    private router: Router
  ) {}

  async submit(): Promise<void> {
    if (this.busy()) return;

    // Непоменянный логин не проверяется (4.3): автоматический `tg123` правилам ввода не отвечает
    const userNameChanged = this.userName.value.trim() !== this.savedUserName();
    this.userName.setValidators(userNameChanged ? userNameValidators : null);
    this.userName.updateValueAndValidity();
    this.userName.markAsTouched();

    const wantsPassword = !this.passwordSet() && this.hasPasswordInput();
    if (wantsPassword) this.passwordForm.markAllAsTouched();

    if (this.userName.invalid || (wantsPassword && this.passwordForm.invalid)) return;

    this.busy.set(true);
    this.userNameRefusal.set(null);
    try {
      if (userNameChanged && !await this.saveUserName()) return;

      if (wantsPassword) {
        const { newPassword, newPasswordConfirm } = this.passwordForm.getRawValue();
        // Задание пароля отзывает прежние токены, текущий тоже — ответ несёт новую сессию
        this.applySession(await this.account.setPassword(newPassword, newPasswordConfirm));
        this.passwordSet.set(true);
      }

      await this.router.navigateByUrl('/');
    } catch (e: unknown) {
      if ((e as { status?: number } | null)?.status === 422) {
        applyServerValidationErrors(this.passwordForm, e);
      } else {
        this.alerts.push('danger', serverErrorMessage(e, this.i18n, this.i18n.t('auth.welcome.failed')), 10000);
      }
    } finally {
      this.busy.set(false);
    }
  }

  /** Логин — первым; ответ — сессия с новым логином. Отказ — под полем и `false` */
  private async saveUserName(): Promise<boolean> {
    try {
      this.applySession(await this.account.changeUserName(this.userName.value.trim()));
      return true;
    } catch (e: unknown) {
      if ((e as { status?: number } | null)?.status === 422) {
        applyServerValidationErrors(this.form, e);
      } else {
        this.userNameRefusal.set(serverErrorMessage(e, this.i18n, this.i18n.t('auth.welcome.failed')));
      }
      return false;
    }
  }

  /** Перевыпуск сессии — тот же человек: активное пространство остаётся */
  private applySession(session: Parameters<UserService['applySession']>[0]): void {
    this.userService.applySession(session, this.workspaceContext.getCurrentWorkspaceId());
  }

  private savedUserName(): string {
    return this.userService.profile()?.userName ?? '';
  }

  /** Пароль необязателен: пустые оба поля — не задаём, начат хоть один — проверяем пару */
  private hasPasswordInput(): boolean {
    const { newPassword, newPasswordConfirm } = this.passwordForm.getRawValue();
    return newPassword !== '' || newPasswordConfirm !== '';
  }
}
