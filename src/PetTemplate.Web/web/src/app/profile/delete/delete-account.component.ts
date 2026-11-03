import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AccountApiService } from '../../shared/api/account-api.service';
import { AccountCardModel } from '../../shared/models/account.model';
import { UserService } from '../../shared/core/user.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { serverErrorMessage } from '../../shared/core/server-error.util';
import { createDeleteConfirmControl } from '../form-models/merge-proof-form.model';
import { injectI18n } from '../../shared/i18n/inject-i18n';
import { PROFILE_SCOPES } from '../profile.scopes';

/**
 * `/profile/delete` — удаление аккаунта (спец. accounts, разделы 9 и 10).
 *
 * Экран показывает, какие пространства удалятся с содержимым (где человек единственный
 * участник) и какие останутся остальным. Подтверждение — свой логин: окна «передумал»
 * нет, удаление физическое и сразу. После удаления сервер этот токен уже не примет —
 * сессия убирается без обращения к нему. Гостя экран сразу уводит на вход
 */
@Component({
  selector: 'profile-delete-account',
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './delete-account.component.html'
})
export class DeleteAccountComponent implements OnInit {
  protected readonly i18n = injectI18n(...PROFILE_SCOPES);

  protected readonly card = signal<AccountCardModel | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly confirm = createDeleteConfirmControl();
  private readonly typed = toSignal(this.confirm.valueChanges, { initialValue: '' });

  protected readonly solo = computed(() => this.card()?.workspaces.filter(x => x.isSolo) ?? []);
  protected readonly shared = computed(() => this.card()?.workspaces.filter(x => !x.isSolo) ?? []);

  /** Кнопка оживает, только когда введён свой логин — регистр не важен, как у входа */
  protected readonly confirmed = computed(() => {
    const card = this.card();
    return !!card && this.typed().trim().toLowerCase() === card.userName.toLowerCase();
  });

  constructor(
    private account: AccountApiService,
    private userService: UserService,
    private alerts: AlertsService,
    private router: Router
  ) {}

  async ngOnInit(): Promise<void> {
    // Адрес открыли без входа: удалять нечего, а перехватчик по 401 уводит только вошедшего
    if (!this.userService.isAuthenticated) {
      await this.router.navigateByUrl('/auth/login');
      return;
    }

    try {
      this.card.set(await this.account.deletePreview());
    } catch (e: unknown) {
      // Сессию, которую сервер уже не принял, перехватчик завершает и уводит на вход сам
      this.error.set(serverErrorMessage(e, this.i18n, this.i18n.t('profile.delete.loadFailed')));
    }
  }

  protected async remove(): Promise<void> {
    if (!this.confirmed() || this.busy()) return;

    this.busy.set(true);
    this.error.set(null);
    try {
      await this.account.deleteAccount(this.confirm.value.trim());
      await this.userService.expireSession();
      this.alerts.push('success', this.i18n.t('profile.delete.done'), 5000);
      await this.router.navigateByUrl('/auth/login');
    } catch (e) {
      this.error.set(serverErrorMessage(e, this.i18n, this.i18n.t('profile.delete.failed')));
    } finally {
      this.busy.set(false);
    }
  }
}
