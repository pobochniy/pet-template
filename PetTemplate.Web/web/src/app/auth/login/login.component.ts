import {Component} from '@angular/core';
import {Router} from '@angular/router';
import {AuthApiService} from '../../shared/api/auth-api.service';
import {createLoginForm} from '../../shared/form-models/login-form.model';
import {AlertsService} from '../../shared/alerts/alerts.service';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
import { FormValidationComponent } from '../../shared/form-validation/form-validation.component';

@Component({
  selector: 'login-auth',
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css'],
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, FormValidationComponent]
})
export class LoginComponent {
  public loginForm = createLoginForm();

  constructor(
    private service: AuthApiService,
    private router: Router,
    private alerts: AlertsService
  ) {}

  async onSubmit() {
    if (!this.loginForm.valid) {
      return;
    }

    try {
      await this.service.login(this.loginForm);
      await this.router.navigateByUrl('/');
    } catch (e: any) {
      if (e.status === 422 && e.error?.ModelState) {
        const errors = this.formatModelStateErrors(e.error.ModelState);
        this.alerts.push('danger', errors, 10000);
      } else {
        this.alerts.push('danger', 'Неверный логин или пароль', 5000);
      }
    }
  }

  private formatModelStateErrors(modelState: { [key: string]: string[] }): string {
    const errors: string[] = [];
    for (const field in modelState) {
      if (modelState.hasOwnProperty(field)) {
        errors.push(...modelState[field]);
      }
    }
    return errors.join('\n');
  }
}
