import { FormControl, FormGroup, Validators } from '@angular/forms';

export interface LoginFormValue {
  login: string;
  password: string;
}

export function createLoginForm(): FormGroup {
  return new FormGroup({
    login: new FormControl('', [Validators.required]),
    password: new FormControl('', [Validators.required, Validators.minLength(6)])
  });
}
