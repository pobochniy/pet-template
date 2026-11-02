import { FormControl, Validators } from '@angular/forms';
import { EMAIL_MAX_LENGTH } from './account-rules';

/**
 * «Забыли пароль?» (спец. accounts, 8.4): одна почта. Логин и телефон не принимаются —
 * письмо уходит на подтверждённую почту, искать аккаунт надо по ней же
 */
export const createRecoverEmailControl = () =>
  new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.email, Validators.maxLength(EMAIL_MAX_LENGTH)]
  });
