import { FormControl, FormGroup, Validators } from '@angular/forms';

/**
 * «Объединить с другим аккаунтом» паролем (спец. accounts, 7.1): логин и пароль второго
 * аккаунта. Длину не проверяем — пароль чужого аккаунта мог быть задан по прежним правилам,
 * сверяет его сервер
 */
export function createMergeProofForm() {
  return new FormGroup({
    login: new FormControl('', { nonNullable: true, validators: Validators.required }),
    password: new FormControl('', { nonNullable: true, validators: Validators.required })
  });
}

/**
 * Подтверждение удаления аккаунта (раздел 9) — свой логин. Совпадение сверяет экран,
 * без учёта регистра, как вход
 */
export function createDeleteConfirmControl() {
  return new FormControl('', { nonNullable: true });
}
