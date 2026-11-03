import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { RecoveryApiService } from '../../shared/api/recovery-api.service';
import { UserService } from '../../shared/core/user.service';
import { LanguageService } from '../../shared/i18n/language.service';
import { FormValidationComponent } from '../../shared/form-validation/form-validation.component';
import { createPasswordForm } from '../../shared/form-models/account-form.model';
import { applyServerValidationErrors } from '../../shared/core/server-validation.util';
import { serverErrorMessage } from '../../shared/core/server-error.util';
import { injectI18n } from '../../shared/i18n/inject-i18n';
import { AUTH_SCOPES } from '../auth.scopes';

/**
 * `/auth/reset/:token` — новый пароль по ссылке из письма или от поддержки
 * (спец. accounts, 8.2): пароль дважды, и сразу вход — без повторного ввода.
 * Ответ сервера — сессия, как у входа: дальше всё как после входа
 */
@Component({
  selector: 'auth-reset',
  imports: [ReactiveFormsModule, RouterLink, FormValidationComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: ['.auth-reset { max-width: 480px; }'],
  template: `
    <div class="container py-4 auth-reset">
      <h4 class="fw-bold text-center mb-4">{{ i18n.t('auth.reset.title') }}</h4>
      <form [formGroup]="form" (ngSubmit)="submit()">
        <div class="mb-3">
          <label class="form-label" for="reset-password">{{ i18n.t('core.field.password') }}</label>
          <input class="form-control" id="reset-password" type="password" autocomplete="new-password"
                 formControlName="newPassword" />
          <form-validation [model]="form.controls.newPassword" fieldKey="core.field.password"></form-validation>
        </div>
        <div class="mb-3">
          <label class="form-label" for="reset-password-confirm">{{ i18n.t('core.field.passwordConfirm') }}</label>
          <input class="form-control" id="reset-password-confirm" type="password" autocomplete="new-password"
                 formControlName="newPasswordConfirm" />
          <form-validation [model]="form.controls.newPasswordConfirm" fieldKey="core.field.passwordConfirm"></form-validation>
          @if (form.controls.newPasswordConfirm.touched && form.hasError('PassNotSame')) {
            <div class="text-danger small mt-1" id="reset-mismatch">{{ i18n.t('core.validation.passwordsDoNotMatch') }}</div>
          }
        </div>
        @if (error()) {
          <div class="alert alert-danger" id="reset-error">
            {{ error() }} <a [routerLink]="['/auth/recover']">{{ i18n.t('auth.reset.again') }}</a>
          </div>
        }
        <div class="d-grid">
          <button type="submit" class="btn btn-primary" id="reset-submit" [disabled]="busy()">
            {{ i18n.t('auth.reset.submit') }}
          </button>
        </div>
      </form>
    </div>
  `
})
export class ResetComponent {
  protected readonly i18n = injectI18n(...AUTH_SCOPES);
  private readonly language = inject(LanguageService);

  /** Токен из адреса — через `withComponentInputBinding()` */
  readonly token = input.required<string>();

  /** Форма «задать пароль»: поле текущего в ней есть, но без проверок и не рисуется */
  protected readonly form = createPasswordForm(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor(
    private recovery: RecoveryApiService,
    private userService: UserService,
    private router: Router
  ) {}

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.busy.set(true);
    this.error.set(null);
    try {
      const { newPassword, newPasswordConfirm } = this.form.getRawValue();
      const session = await this.recovery.resetPassword(this.token(), newPassword, newPasswordConfirm);
      this.userService.applySession(session);
      await this.language.adopt(session.profile?.language);

      await this.router.navigateByUrl('/');
    } catch (e: unknown) {
      if ((e as { status?: number } | null)?.status === 422) applyServerValidationErrors(this.form, e);
      else this.error.set(serverErrorMessage(e, this.i18n, this.i18n.t('auth.error.linkInvalid')));
    } finally {
      this.busy.set(false);
    }
  }
}
