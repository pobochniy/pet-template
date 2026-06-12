import { FormControl, FormGroup, Validators } from "@angular/forms";

/** Модель на странице профиля */
export let profileFormModel = new FormGroup({
  /** UserName */
  userName: new FormControl('', [Validators.required, Validators.minLength(2), Validators.maxLength(20)]),

  /** Комментарий */
  comment: new FormControl('', Validators.maxLength(3000)),

  /** Есть 18+ */
  isAdult: new FormControl(false, Validators.requiredTrue),

  /** Согласен с правилами использования */
  hasAcceptedTerms: new FormControl(false, Validators.requiredTrue)
});
