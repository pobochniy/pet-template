import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { createProfileForm } from '../../shared/form-models/profile-form.model';
import { ProfileApiService } from '../../shared/api/profile-api.service';
import { FormValidationComponent } from '../../shared/form-validation/form-validation.component';
import { UserService } from '../../shared/core/user.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { ProfileLoginsComponent } from '../logins/logins.component';

/**
 * Свой профиль: блок «Основное» (логин, пароль, контакты — `ProfileLoginsComponent`,
 * спец. accounts, 6), аватар и подтверждения (`Profile/Save`)
 */
@Component({
  selector: 'app-edit',
  templateUrl: './edit.component.html',
  styleUrl: './edit.component.css',
  imports: [ReactiveFormsModule, FormValidationComponent, RouterLink, ProfileLoginsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class EditComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
  protected readonly userService = inject(UserService);
  private readonly alerts = inject(AlertsService);

  public readonly profileForm = createProfileForm();

  protected readonly avatarUrl = computed(() => `/avatars/${this.userService.profile()?.avatarId ?? 0}.png`);

  constructor(private service: ProfileApiService, private router: Router) {
  }

  async ngOnInit() {
    const profile = await this.service.details();
    if (!profile) return;

    this.userService.setProfile(profile);

    this.profileForm.setValue({
      isAdult: profile.isAdult,
      hasAcceptedTerms: profile.hasAcceptedTerms
    });
  }

  async onSubmit() {
    this.profileForm.markAllAsTouched();
    if (!this.profileForm.valid) return;

    try {
      // Ответ — профиль, каким он лёг в базу (`Profile/Save`): им и обновляем сессию
      const saved = await this.service.save(this.profileForm);
      if (saved) this.userService.setProfile({ ...saved, primaryWorkspaceId: this.userService.profile()?.primaryWorkspaceId });
      await this.router.navigateByUrl('/');
    } catch {
      this.alerts.push('danger', this.i18n.t('profile.edit.saveFailed'), 10000);
    }
  }
}
