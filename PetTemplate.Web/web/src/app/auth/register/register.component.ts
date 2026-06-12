import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { registerFormModel } from '../../shared/form-models/register-form.model';
import { AuthApiService } from '../../shared/api/auth-api.service';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
import { FormValidationComponent } from '../../shared/form-validation/form-validation.component';
import { AlertsService } from '../../shared/alerts/alerts.service';

@Component({
  selector: 'login-register',
  templateUrl: './register.component.html',
  styleUrls: ['./register.component.css'],
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, FormValidationComponent]
})
export class RegisterComponent {

  public registerForm = registerFormModel;

  constructor(
    private service: AuthApiService,
    private router: Router,
    private alerts: AlertsService
  ) {}

  async onSubmit() {
    Object.keys(this.registerForm.controls).forEach(key => {
      this.registerForm.get(key)?.markAsDirty();
    });

    try {
      if (this.registerForm.valid) {
        await this.service.register(this.registerForm);
        await this.router.navigateByUrl('/');
      }
    } catch (e: any) {
      this.alerts.push('danger', 'Ошибка регистрации', 10000);
    }
  }
}
