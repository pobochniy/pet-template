import { AbstractControl, FormControl, FormGroup, ValidationErrors, Validators } from '@angular/forms';
import { WORKSPACE_NAME_MAX_LENGTH, trimmedRequired } from '../../shared/form-models/workspace-form.model';
import { emailValidators, passwordValidators, phoneValidator, userNameValidator } from '../../shared/form-models/account-rules';

function passwordMatchValidator(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value;
  const passwordConfirm = control.get('passwordConfirm')?.value;
  return password === passwordConfirm ? null : { PassNotSame: true };
}

/**
 * Форма регистрации. Фабрика, а не общий экземпляр: значения не переживают экран.
 *
 * Название пространства обязательно (спец. authorization, 3, п. 1): регистрация
 * и пространство — один шаг. «Меня пригласят — пропустить» снимает поле
 * (`setWorkspaceSkipped`), и регистрация идёт без пространства
 */
export function createRegisterForm() {
  return new FormGroup(
    {
      /** Логин — правила спец. accounts, 4.1. Имя поля — как у сервера: 422 встаёт под него */
      userName: new FormControl('', [Validators.required, userNameValidator]),
      /** Пароль — от 8 до 100 символов (8.1) */
      password: new FormControl('', passwordValidators),
      passwordConfirm: new FormControl('', [Validators.required]),
      workspaceName: new FormControl('', [trimmedRequired, Validators.maxLength(WORKSPACE_NAME_MAX_LENGTH)]),
      /** Телефон — плюс и 11 цифр, иначе им не войти. Под «Дополнительными полями» */
      phone: new FormControl('', phoneValidator),
      email: new FormControl('', emailValidators)
    },
    { validators: passwordMatchValidator }
  );
}

/**
 * Пропуск пространства: выключенное поле не проверяется и не уходит в `value` —
 * сервер получит регистрацию без названия. Вернуть — снова включить
 */
export function setWorkspaceSkipped(form: ReturnType<typeof createRegisterForm>, skipped: boolean): void {
  const control = form.controls.workspaceName;

  if (skipped) control.disable();
  else control.enable();
}
