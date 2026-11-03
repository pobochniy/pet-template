import { AbstractControl, FormControl, FormGroup, ValidationErrors, Validators } from '@angular/forms';

/** Ограничение из `WorkspaceConstants.NameMaxLength` (`PetTemplate.Shared/Constants`) */
export const WORKSPACE_NAME_MAX_LENGTH = 100;

/**
 * Обязательное поле, где одни пробелы не считаются значением: сервер обрезает
 * их (`TrimmedStringLength`) и ответил бы 422 на то, что форма пропустила
 */
export function trimmedRequired(control: AbstractControl): ValidationErrors | null {
  const value = control.value as string | null;

  return value != null && value.trim().length > 0 ? null : { required: true };
}

/**
 * Форма создания и переименования пространства. Фабрика, а не общий экземпляр:
 * форму открывают несколько раз подряд (создал одно — заводит второе), и общий
 * `FormGroup` протаскивал бы прошлое название
 */
export function createWorkspaceForm(name = '') {
  return new FormGroup({
    name: new FormControl(name, {
      nonNullable: true,
      validators: [trimmedRequired, Validators.maxLength(WORKSPACE_NAME_MAX_LENGTH)]
    })
  });
}
