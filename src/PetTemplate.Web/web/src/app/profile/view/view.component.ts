import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ProfileApiService } from '../profile-api.service';
import { ProfileModel } from '../../shared/models/profile.model';
import { injectI18n } from '../../shared/i18n/inject-i18n';
import { PROFILE_SCOPES } from '../profile.scopes';

@Component({
  selector: 'app-view',
  templateUrl: './view.component.html',
  styleUrl: './view.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ViewComponent implements OnInit {
  protected readonly i18n = injectI18n(...PROFILE_SCOPES);

  /** Сигнал, а не поле: профиль приезжает после первой отрисовки, а Zone.js, которая заметила бы присваивание, нет */
  readonly profile = signal<ProfileModel | undefined>(undefined);
  readonly userId: string | null;

  constructor(
    private service: ProfileApiService,
    route: ActivatedRoute,
    private router: Router
  ) {
    this.userId = route.snapshot.paramMap.get('userId');
  }

  async ngOnInit() {
    if (!this.userId) {
      await this.router.navigateByUrl('/');
      return;
    }

    const profile = await this.service.view(this.userId);
    this.profile.set(profile ?? undefined);

    if (!profile) await this.router.navigateByUrl('/');
  }
}
