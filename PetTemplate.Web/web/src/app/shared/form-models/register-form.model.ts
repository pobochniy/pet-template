import { FormControl, FormGroup, Validators, AbstractControl, ValidationErrors } from '@angular/forms';

function passwordMatchValidator(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value;
  const passwordConfirm = control.get('passwordConfirm')?.value;
  return password === passwordConfirm ? null : { PassNotSame: true };
}

export const registerFormModel = new FormGroup(
  {
    username: new FormControl('', [Validators.required]),
    password: new FormControl('', [Validators.required, Validators.minLength(6)]),
    passwordConfirm: new FormControl('', [Validators.required]),
    phone: new FormControl(''),
    email: new FormControl('', [Validators.email])
  },
  { validators: passwordMatchValidator }
);
