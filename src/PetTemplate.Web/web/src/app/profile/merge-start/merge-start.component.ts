import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AccountApiService } from '../../shared/api/account-api.service';
import { ExternalLoginService } from '../../shared/core/external-login.service';
import { MergeStateService } from '../../shared/core/merge-state.service';
import { ExternalProvider } from '../../shared/models/external-login.model';
import { MergeProofModel } from '../../shared/models/account.model';
import { I18nService } from '../../shared/i18n/i18n.service';
import { serverErrorMessage } from '../../shared/core/server-error.util';
import { createMergeProofForm } from '../../shared/form-models/merge-proof-form.model';
import { FreshLoginService } from '../fresh-login/fresh-login.service';

/**
 * «Объединить с другим аккаунтом» в блоке «Основное» (спец. accounts, 7.1): владение
 * вторым доказывается его логином и паролем, его Telegram или его VK. Ответ — просмотр
 * слияния, его показывает `/profile/merge`.
 *
 * Telegram и VK — тот же путь, что вход, с пометкой «слияние»: страница возврата
 * отдаст доказательство в `Account/MergePreview` и откроет экран слияния. Кнопки
 * площадок — только для настроенных на сервере.
 *
 * Владение текущим — свежим входом (8.6): подтверждение спрашивается до ухода на площадку
 * (`ensure()`), а просмотр по паролю идёт через `run()` — окно могло закрыться по дороге
 */
@Component({
  selector: 'profile-merge-start',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button type="button" class="btn btn-link p-0" id="merge-start" (click)="open.set(!open())">
      {{ i18n.t('profile.merge.start') }}
    </button>
    @if (open()) {
      <div class="mt-2" id="merge-start-form">
        <div class="form-text mb-2">{{ i18n.t('profile.merge.startHint') }}</div>
        <div class="fw-semibold small mb-1">{{ i18n.t('profile.merge.startPassword') }}</div>
        <div class="input-group input-group-sm mb-2">
          <input class="form-control" id="merge-login" type="text" autocomplete="off"
                 [formControl]="form.controls.login" [class.is-invalid]="form.controls.login.touched && form.controls.login.invalid"
                 [attr.aria-label]="i18n.t('auth.field.login')" [placeholder]="i18n.t('auth.field.login')" />
          <input class="form-control" id="merge-password" type="password" autocomplete="off"
                 [formControl]="form.controls.password" [class.is-invalid]="form.controls.password.touched && form.controls.password.invalid"
                 [attr.aria-label]="i18n.t('core.field.password')" [placeholder]="i18n.t('core.field.password')" />
          <button type="button" class="btn btn-primary" id="merge-by-password" [disabled]="busy()" (click)="byPassword()">
            {{ i18n.t('profile.merge.startSubmit') }}
          </button>
        </div>
        <div class="d-flex flex-wrap gap-2">
          @if (enabled().telegram) {
            <button type="button" class="btn btn-sm btn-outline-primary" id="merge-by-telegram" [disabled]="busy()" (click)="byPlatform('telegram')">
              {{ i18n.t('profile.merge.startTelegram') }}
            </button>
          }
          @if (enabled().vk) {
            <button type="button" class="btn btn-sm btn-outline-primary" id="merge-by-vk" [disabled]="busy()" (click)="byPlatform('vk')">
              {{ i18n.t('profile.merge.startVk') }}
            </button>
          }
        </div>
        @if (error()) {
          <div class="text-danger small mt-1" id="merge-start-error">{{ error() }}</div>
        }
      </div>
    }
  `
})
export class ProfileMergeStartComponent {
  protected readonly i18n = inject(I18nService);

  /** Площадки, настроенные на сервере: без них кнопке вести некуда */
  readonly enabled = input<Record<ExternalProvider, boolean>>({ telegram: false, vk: false });

  protected readonly open = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = createMergeProofForm();

  constructor(
    private account: AccountApiService,
    private external: ExternalLoginService,
    private mergeState: MergeStateService,
    private router: Router,
    private fresh: FreshLoginService
  ) {}

  protected async byPassword(): Promise<void> {
    // Пустое поле — подсветить, а не молча не ответить на нажатие
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const { login, password } = this.form.getRawValue();
    await this.preview({ login: login.trim(), password });
  }

  protected async byPlatform(provider: ExternalProvider): Promise<void> {
    this.error.set(null);
    if (!(await this.fresh.ensure())) return;

    this.busy.set(true);
    try {
      await this.external.start(provider, 'merge', '/profile/merge');
    } catch (e) {
      this.error.set(serverErrorMessage(e, this.i18n, this.i18n.t('profile.merge.failed')));
    } finally {
      this.busy.set(false);
    }
  }

  private async preview(proof: MergeProofModel): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const preview = await this.fresh.run(() => this.account.mergePreview(proof));
      if (!preview) return;

      this.mergeState.open(preview);
      await this.router.navigateByUrl('/profile/merge');
    } catch (e: unknown) {
      // Неверная пара — та же своя фраза, что на входе: подробности не нужны
      this.error.set((e as { status?: number } | null)?.status === 422
        ? this.i18n.t('auth.error.invalidCredentials')
        : serverErrorMessage(e, this.i18n, this.i18n.t('profile.merge.failed')));
    } finally {
      this.busy.set(false);
    }
  }
}
