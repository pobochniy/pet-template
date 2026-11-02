import { AbstractControl, ValidationErrors, Validators } from '@angular/forms';

/**
 * Правила логина, пароля и телефона (спец. accounts, 4.1 и 8.1) — копия `UserNameRule`,
 * `PasswordRule` и `PhoneRule` из `src/PetTemplate.Shared/Validation`. Сервер проверяет всё
 * сам; здесь — чтобы человек увидел отказ до отправки. Правится одно — правится и второе
 */
export const USER_NAME_MIN_LENGTH = 3;
export const USER_NAME_MAX_LENGTH = 20;

export const PASSWORD_MIN_LENGTH = 8;
export const PASSWORD_MAX_LENGTH = 100;

/** Почта — до 255 символов: у `citext` в базе предела нет, его держат проверки (спец. accounts, 8.4) */
export const EMAIL_MAX_LENGTH = 255;

const USER_NAME_FORMAT = /^[A-Za-z][A-Za-z0-9_.]*$/;

/** `tg<цифры>` и `vk<цифры>` — автоматические логины (4.2), человеку не достаются */
const USER_NAME_RESERVED = /^(tg|vk)[0-9]+$/i;

const PHONE_FORMAT = /^\+[0-9]{11}$/;

/**
 * Логин по правилам 4.1. Ключи ошибок совпадают с хвостами кодов сервера
 * (`core.validation.userNameFormat`, `core.validation.userNameReserved`): один перевод
 * на оба источника отказа. Пустое значение пропускает — обязательность задаёт
 * `Validators.required`
 */
export function userNameValidator(control: AbstractControl): ValidationErrors | null {
  const value = typeof control.value === 'string' ? control.value.trim() : '';
  if (value.length === 0) return null;

  if (value.length < USER_NAME_MIN_LENGTH || value.length > USER_NAME_MAX_LENGTH || !USER_NAME_FORMAT.test(value)) {
    return { userNameFormat: { min: USER_NAME_MIN_LENGTH, max: USER_NAME_MAX_LENGTH } };
  }

  return USER_NAME_RESERVED.test(value) ? { userNameReserved: true } : null;
}

/** Телефон — плюс и 11 цифр: другим видом не войти. Пустое — «нет телефона» */
export function phoneValidator(control: AbstractControl): ValidationErrors | null {
  const value = typeof control.value === 'string' ? control.value.trim() : '';
  if (value.length === 0) return null;

  return PHONE_FORMAT.test(value) ? null : { phoneFormat: true };
}

/** Новый пароль: обязателен, от 8 до 100 символов */
export const passwordValidators = [
  Validators.required,
  Validators.minLength(PASSWORD_MIN_LENGTH),
  Validators.maxLength(PASSWORD_MAX_LENGTH)
];

/** Почта: формат и предел длины. Пустая законна — «почты нет» */
export const emailValidators = [Validators.email, Validators.maxLength(EMAIL_MAX_LENGTH)];
