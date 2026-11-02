import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProfileApiService } from '../../shared/api/profile-api.service';
import { UserService } from '../../shared/core/user.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { I18nService } from '../../shared/i18n/i18n.service';

@Component({
  selector: 'app-avatar',
  imports: [RouterLink],
  templateUrl: './avatar.html',
  styleUrl: './avatar.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Avatar implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly profileApi = inject(ProfileApiService);
  private readonly alerts = inject(AlertsService);
  readonly userService = inject(UserService);

  readonly availableAvatars = [0, 1, 2, 3, 4, 5, 6, 7];
  readonly currentAvatarId = computed(() => this.userService.profile()?.avatarId ?? 0);

  async ngOnInit() {
    // Загружаем профиль, если он еще не загружен
    if (!this.userService.profile()) {
      const profile = await this.profileApi.details();
      if (profile) this.userService.setProfile(profile);
    }
  }

  async selectAvatar(avatarId: number) {
    try {
      await this.profileApi.setAvatar(avatarId);

      const profile = this.userService.profile();
      if (profile) this.userService.setProfile({ ...profile, avatarId });
    } catch {
      this.alerts.push('danger', this.i18n.t('profile.avatar.setFailed'), 5000);
    }
  }

  getAvatarUrl(id: number): string {
    return `/avatars/${id}.png`;
  }
}
