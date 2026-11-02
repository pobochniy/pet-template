import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { AlertsService } from './alerts.service';
import { I18nService } from '../i18n/i18n.service';

@Component({
  selector: 'shared-alerts',
  templateUrl: './alerts.component.html',
  styleUrl: './alerts.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AlertsComponent {
  protected readonly i18n = inject(I18nService);

  constructor(public alertsService: AlertsService) { }

  public removeAlert(id: number) {
    this.alertsService.remove(id);
  }
}
