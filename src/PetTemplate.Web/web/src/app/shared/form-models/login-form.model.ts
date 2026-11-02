import { FormControl, FormGroup, Validators } from '@angular/forms';
import { PASSWORD_MAX_LENGTH } from './account-rules';

/**
 * Вход: логин, почта или телефон и пароль. Нижняя граница пароля — прежняя (3): новые
 * правила (спец. accounts, 8.1) на вход не влияют, короткие пароли шаблона v1 входят как
 * входили (`PasswordRule.LegacyMinLength` на сервере)
 */
export function createLoginForm() {
  return new FormGroup({
    login: new FormControl('', [Validators.required, Validators.maxLength(255)]),
    password: new FormControl('', [Validators.required, Validators.minLength(3), Validators.maxLength(PASSWORD_MAX_LENGTH)])
  });
}
