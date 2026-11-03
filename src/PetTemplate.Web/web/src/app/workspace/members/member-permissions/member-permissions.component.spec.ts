import type { Mocked } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { WorkspaceMemberPermissionsComponent } from './member-permissions.component';
import { UsersApiService } from '../../users-api.service';
import { UserService } from '../../../shared/core/user.service';
import { PermissionEnum } from '../../../shared/enums/permission.enum';
import { I18nService } from '../../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../../shared/i18n/i18n.testing';
import { profileDto, sessionDto } from '../../../shared/api/dto.mock';

/** Правка прав участника — то, во что вырос экран `role-management` шаблона */
describe('WorkspaceMemberPermissionsComponent', () => {
  let fixture: ComponentFixture<WorkspaceMemberPermissionsComponent>;
  let el: HTMLElement;
  let api: Mocked<UsersApiService>;

  async function render(own: PermissionEnum[], granted: PermissionEnum[]) {
    api = { getUserRoles: vi.fn(), setUserRoles: vi.fn() } as unknown as Mocked<UsersApiService>;
    api.getUserRoles.mockResolvedValue(granted);
    api.setUserRoles.mockResolvedValue(undefined);

    await TestBed.configureTestingModule({
      imports: [WorkspaceMemberPermissionsComponent],
      providers: [provideZonelessChangeDetection(), { provide: UsersApiService, useValue: api }]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));
    TestBed.inject(UserService).applySession(sessionDto({
      token: 'token',
      profile: profileDto({ id: 'me', userName: 'Oleg', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true }),
      permissionsByWorkspace: { 'ws-1': own }
    }));

    fixture = TestBed.createComponent(WorkspaceMemberPermissionsComponent);
    fixture.componentRef.setInput('userId', 'u-vasya');
    el = fixture.nativeElement;
    await fixture.whenStable();
  }

  const checkbox = (permission: PermissionEnum) => el.querySelector<HTMLInputElement>(`#chbox-${permission}`);
  const save = () => el.querySelector<HTMLButtonElement>('#save-roles')!;

  it('should tick the permissions the member already has, in the active workspace', async () => {
    await render([PermissionEnum.PermissionManagement, PermissionEnum.WorkspaceMemberRead], [PermissionEnum.WorkspaceMemberRead]);

    expect(api.getUserRoles).toHaveBeenCalledWith('u-vasya', 'ws-1');
    expect(checkbox(PermissionEnum.WorkspaceMemberRead)!.checked).toBe(true);
    expect(checkbox(PermissionEnum.PermissionManagement)!.checked).toBe(false);
  });

  // В галках только свои права — плюс те, что участнику уже выдал кто-то более правый
  it('should offer only own permissions and ones the member already has', async () => {
    await render([PermissionEnum.PermissionManagement], [PermissionEnum.WorkspaceInfoCrud]);

    expect(checkbox(PermissionEnum.PermissionManagement)).not.toBeNull();
    expect(checkbox(PermissionEnum.WorkspaceInfoCrud)).not.toBeNull();
    expect(checkbox(PermissionEnum.WorkspaceMemberCrud)).toBeNull();
  });

  it('should save the ticked permissions for the active workspace', async () => {
    await render([PermissionEnum.PermissionManagement, PermissionEnum.WorkspaceMemberRead], [PermissionEnum.WorkspaceMemberRead]);

    checkbox(PermissionEnum.PermissionManagement)!.click();
    save().click();
    await fixture.whenStable();

    expect(api.setUserRoles).toHaveBeenCalledWith('u-vasya', 'ws-1',
      [PermissionEnum.WorkspaceMemberRead, PermissionEnum.PermissionManagement]);
  });

  // Последнюю галку снять можно, а сохранить — нет: пустой набор — уже не участник
  it('should not save an empty set', async () => {
    await render([PermissionEnum.PermissionManagement, PermissionEnum.WorkspaceMemberRead], [PermissionEnum.WorkspaceMemberRead]);

    checkbox(PermissionEnum.WorkspaceMemberRead)!.click();
    await fixture.whenStable();

    expect(save().disabled).toBe(true);
    expect(el.querySelector('#no-roles-hint')).not.toBeNull();
  });

  // Отказ 422 у поля прав показывается под галками, переведённым по коду
  it('should show a field-level refusal under the permissions', async () => {
    await render([PermissionEnum.PermissionManagement, PermissionEnum.WorkspaceMemberRead], [PermissionEnum.WorkspaceMemberRead]);
    api.setUserRoles.mockRejectedValue(new HttpErrorResponse({
      status: 422,
      error: { permissions: [{ code: 'workspace.error.emptyPermissions' }] }
    }));

    save().click();
    await fixture.whenStable();

    expect(el.querySelector('.permissions-field-error')?.textContent?.trim())
      .toBe('Оставьте участнику хотя бы одно право. Чтобы убрать человека из пространства, удалите участника');
  });
});
