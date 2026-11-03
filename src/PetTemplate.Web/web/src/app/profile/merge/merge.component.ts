import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AccountApiService } from '../../shared/api/account-api.service';
import { MergeStateService } from '../../shared/core/merge-state.service';
import { UserService } from '../../shared/core/user.service';
import { AccountCardModel, AccountWorkspaceModel } from '../../shared/models/account.model';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { formatDate, formatNumber } from '../../shared/i18n/format.util';
import { serverErrorMessage } from '../../shared/core/server-error.util';
import { injectI18n } from '../../shared/i18n/inject-i18n';
import { PROFILE_SCOPES } from '../profile.scopes';

/**
 * Экран слияния (спец. accounts, 7.3): два аккаунта рядом — логин, способы войти, дата
 * регистрации, пространства с их судьбой. По умолчанию не выбран ни один: выбор необратим,
 * и подсказка «оставьте тот, где больше данных» стоит текстом, а не предвыбором.
 *
 * Подтверждение — отдельным шагом, словами; кнопка — «Удалить X и оставить Y», а не
 * «Подтвердить». Отменить слияние нельзя.
 *
 * Просмотр приезжает из `MergeStateService`: его кладут привязка занятого входа, развилка
 * mini-app и «Объединить с другим аккаунтом». В адрес он не кладётся — там ключ слияния.
 * Гостя экран сразу уводит на вход
 */
@Component({
  selector: 'profile-merge',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './merge.component.html'
})
export class MergeComponent implements OnInit {
  protected readonly i18n = injectI18n(...PROFILE_SCOPES);
  private readonly state = inject(MergeStateService);

  protected readonly preview = this.state.preview;
  protected readonly keepId = signal<string | null>(null);
  protected readonly confirming = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly accounts = computed(() => {
    const preview = this.preview();
    return preview ? [preview.current, preview.other] : [];
  });

  protected readonly kept = computed(() => this.accounts().find(x => x.userId === this.keepId()) ?? null);
  protected readonly removed = computed(() => this.keepId() ? this.accounts().find(x => x.userId !== this.keepId()) ?? null : null);

  /** Telegram и VK удаляемого переходят, только если у остающегося место свободно (7.2) */
  protected readonly movingLogins = computed(() => {
    const kept = this.kept();
    const removed = this.removed();
    if (!kept || !removed) return [];
    return [
      removed.hasTelegram && !kept.hasTelegram ? 'Telegram' : null,
      removed.hasVk && !kept.hasVk ? 'VK' : null
    ].filter((x): x is string => !!x);
  });

  /** Telegram и VK удаляемого, чьё место у остающегося занято, пропадают вместе с ним (7.2) */
  protected readonly droppedLogins = computed(() => {
    const kept = this.kept();
    const removed = this.removed();
    if (!kept || !removed) return [];
    return [
      removed.hasTelegram && kept.hasTelegram ? 'Telegram' : null,
      removed.hasVk && kept.hasVk ? 'VK' : null
    ].filter((x): x is string => !!x);
  });

  constructor(
    private account: AccountApiService,
    private userService: UserService,
    private alerts: AlertsService,
    private router: Router
  ) {}

  async ngOnInit(): Promise<void> {
    if (!this.userService.isAuthenticated) await this.router.navigateByUrl('/auth/login');
  }

  protected logins(card: AccountCardModel): string {
    const ways = [
      card.hasPassword ? this.i18n.t('profile.merge.password') : null,
      card.hasTelegram ? 'Telegram' : null,
      card.hasVk ? 'VK' : null
    ].filter((x): x is string => !!x);
    return ways.length ? ways.join(', ') : this.i18n.t('profile.merge.noLogins');
  }

  protected registered(card: AccountCardModel): string {
    return formatDate(this.i18n.lang(), card.registeredAtUtc);
  }

  protected n(value: number): string {
    return formatNumber(this.i18n.lang(), value);
  }

  /** Что станет с пространством, если удалится этот аккаунт */
  protected fate(workspace: AccountWorkspaceModel): string {
    return workspace.isSolo ? this.i18n.t('profile.merge.fateDeleted') : this.i18n.t('profile.merge.fateMoves');
  }

  /**
   * Оставить можно аккаунт, если второй можно удалить: аккаунт с ролью в админке только
   * остаётся (7.3). Второй, заведённый меньше суток назад, не остаётся вовсе — остаётся текущий
   */
  protected canKeep(card: AccountCardModel): boolean {
    if (this.isYoungOther(card)) return false;
    return this.accounts().every(x => x.userId === card.userId || x.canBeRemoved);
  }

  protected isYoungOther(card: AccountCardModel): boolean {
    const preview = this.preview();
    return !!preview && card.userId === preview.other.userId && !preview.canKeepOther;
  }

  protected choose(userId: string): void {
    this.keepId.set(userId);
    this.confirming.set(false);
    this.error.set(null);
  }

  protected async merge(): Promise<void> {
    const preview = this.preview();
    const keepId = this.keepId();
    if (!preview || !keepId || this.busy()) return;

    this.busy.set(true);
    this.error.set(null);
    try {
      const session = await this.account.merge(preview.ticket, keepId);
      // Остаётся не тот, из которого начинали, — без новой сессии клиент оказался бы
      // в удалённом; остаётся текущий — его права поменялись
      this.userService.applySession(session);
      this.state.clear();
      this.alerts.push('success', this.i18n.t('profile.merge.done'), 5000);
      await this.router.navigateByUrl('/profile/edit');
    } catch (e: unknown) {
      this.error.set(serverErrorMessage(e, this.i18n, this.i18n.t('profile.merge.failed')));
      // Ключ одноразовый: после 409 и 400 он уже погашен — просматривать придётся заново
      const status = (e as { status?: number } | null)?.status;
      if (status === 409 || status === 400) this.state.clear();
    } finally {
      this.busy.set(false);
    }
  }
}
