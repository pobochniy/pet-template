import { ChangeDetectionStrategy, Component } from '@angular/core';
import { AlertsService } from './alerts.service';
import { injectI18n } from '../i18n/inject-i18n';

@Component({
  selector: 'shared-alerts',
  templateUrl: './alerts.component.html',
  styleUrl: './alerts.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AlertsComponent {
  protected readonly i18n = injectI18n();

  constructor(public alertsService: AlertsService) { }

  public removeAlert(id: number) {
    this.alertsService.remove(id);
  }
}
