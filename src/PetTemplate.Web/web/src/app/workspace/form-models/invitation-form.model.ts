import { AbstractControl, FormControl, FormGroup, FormRecord, ValidationErrors, Validators } from '@angular/forms';
import { PermissionEnum } from '../../shared/enums/permission.enum';

/** Ограничение из `InvitationConstants.InviteeNameMaxLength` (`PetTemplate.Shared/Constants`) */
export const INVITEE_NAME_MAX_LENGTH = 50;

/**
 * Хотя бы одно право должно остаться отмеченным: приглашение без прав не даёт
 * даже войти в пространство, и сервер такое отклоняет (422). Кнопку блокируем
 * до отправки, чтобы отказ не приезжал с сервера на очевидном
 */
function atLeastOnePermission(control: AbstractControl): ValidationErrors | null {
  const value = control.value as Record<string, boolean>;

  return Object.values(value ?? {}).some(Boolean) ? null : { noPermissions: true };
}

/**
 * Форма выдачи приглашения. Набор чекбоксов зависит от того, какие права есть
 * у самого пригласившего — выдать больше, чем есть, сервер всё равно не даст,
 * поэтому недоступных прав в форме просто нет. Все доступные проставлены
 */
export function createInvitationForm(available: readonly PermissionEnum[]) {
  const form = new FormGroup({
    inviteeName: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(INVITEE_NAME_MAX_LENGTH)]
    }),
    permissions: new FormRecord<FormControl<boolean>>({}, { validators: atLeastOnePermission })
  });

  syncInvitationPermissions(form.controls.permissions, available);

  return form;
}

/**
 * Приводит набор чекбоксов к списку доступных прав. Права меняются под открытой
 * страницей — например при смене пространства, — и пересобирать форму целиком нельзя:
 * уже снятые галочки и набранное имя пропали бы. Поэтому правим только разницу,
 * новое право приходит отмеченным (как и при открытии формы)
 */
export function syncInvitationPermissions(
  group: FormRecord<FormControl<boolean>>,
  available: readonly PermissionEnum[]
): void {
  const keys = available.map(x => String(x));

  for (const key of Object.keys(group.controls)) {
    if (!keys.includes(key)) group.removeControl(key);
  }

  for (const key of keys) {
    if (!group.contains(key)) group.addControl(key, new FormControl<boolean>(true, { nonNullable: true }));
  }
}

/** Отмеченные права — в том виде, в каком их ждёт сервер */
export function selectedPermissions(group: FormRecord<FormControl<boolean>>): PermissionEnum[] {
  return Object.entries(group.getRawValue())
    .filter(([, checked]) => checked)
    .map(([permission]) => Number(permission) as PermissionEnum);
}
