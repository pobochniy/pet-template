import { FormControl, FormGroup, Validators } from '@angular/forms';

/**
 * Форма подтверждений профиля. Фабрика, а не общий экземпляр на модуль: иначе форма
 * переживала бы экран и выход — следующий вошедший увидел бы чужие значения.
 *
 * Логина и комментария здесь нет: они в блоке «Основное» (`account-form.model.ts`,
 * спец. accounts, 6) — у логина свой вызов с отказом «занят»
 */
export function createProfileForm() {
  return new FormGroup({
    /** Есть 18+ */
    isAdult: new FormControl(false, { nonNullable: true, validators: Validators.requiredTrue }),

    /** Согласен с правилами использования */
    hasAcceptedTerms: new FormControl(false, { nonNullable: true, validators: Validators.requiredTrue })
  });
}

export type ProfileForm = ReturnType<typeof createProfileForm>;
