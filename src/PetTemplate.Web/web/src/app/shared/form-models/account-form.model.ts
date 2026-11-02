import { AbstractControl, FormControl, FormGroup, ValidationErrors, Validators } from '@angular/forms';
import { emailValidators, passwordValidators, phoneValidator, userNameValidator } from './account-rules';

/**
 * Блок «Основное» профиля (спец. accounts, 6): логин, почта, телефон и комментарий —
 * под одной кнопкой «Сохранить». Логин уходит своим вызовом (`Account/ChangeUserName`),
 * остальное — `Account/Save`.
 *
 * Логин без `required` и правил формата на самой форме: непоменянный логин не проверяется
 * (4.3) — автоматический `tg123` правилам ввода не отвечает и сохранить остальное
 * мешать не должен. Проверяет его экран, когда логин поменяли
 */
export const createAccountForm = () => new FormGroup({
  userName: new FormControl('', { nonNullable: true }),
  email: new FormControl('', { nonNullable: true, validators: emailValidators }),
  phone: new FormControl('', { nonNullable: true, validators: phoneValidator }),
  comment: new FormControl('', { nonNullable: true, validators: Validators.maxLength(3000) })
});

/** Правила нового логина — их ставит экран, когда логин поменяли */
export const userNameValidators = [Validators.required, userNameValidator];

/** Новый пароль дважды — ошибка `PassNotSame` на всей группе, как у регистрации */
function newPasswordsMatch(control: AbstractControl): ValidationErrors | null {
  return control.get('newPassword')?.value === control.get('newPasswordConfirm')?.value
    ? null
    : { PassNotSame: true };
}

/**
 * «Задать пароль» (без текущего) или «Сменить пароль» (с текущим) — спец. accounts, 8.1.
 * У формы «задать» поле текущего пароля есть, но без проверок и в запрос не уходит:
 * одна модель на обе формы, а шаблон просто его не рисует
 */
export const createPasswordForm = (withCurrent: boolean) => new FormGroup({
  currentPassword: new FormControl('', { nonNullable: true, validators: withCurrent ? Validators.required : [] }),
  newPassword: new FormControl('', { nonNullable: true, validators: passwordValidators }),
  newPasswordConfirm: new FormControl('', { nonNullable: true, validators: Validators.required })
}, { validators: newPasswordsMatch });
