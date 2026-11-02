import { AbstractControl, FormControl, FormGroup, ValidationErrors, Validators } from '@angular/forms';
import { passwordValidators, userNameValidator } from './account-rules';

function passwordMatch(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value;
  const passwordConfirm = control.get('passwordConfirm')?.value;

  return password === passwordConfirm ? null : { PassNotSame: true };
}

/**
 * Регистрация по ссылке-приглашению — только логин и пароль.
 *
 * Почту и телефон не спрашиваем намеренно: их занятость — единственный способ,
 * которым анонимная форма могла бы подтвердить существование чужого аккаунта
 * по чужому адресу. Занятость логина неизбежна в любой системе с уникальными
 * никами и ничего нового не открывает (спец. authorization, 6.3).
 *
 * Правила логина и пароля — те же, что у обычной регистрации (`account-rules.ts`,
 * спец. accounts, 4.1 и 8.1)
 */
export function createInviteRegisterForm() {
  return new FormGroup(
    {
      userName: new FormControl('', { nonNullable: true, validators: [Validators.required, userNameValidator] }),
      password: new FormControl('', { nonNullable: true, validators: passwordValidators }),
      passwordConfirm: new FormControl('', { nonNullable: true, validators: [Validators.required] })
    },
    { validators: passwordMatch }
  );
}
