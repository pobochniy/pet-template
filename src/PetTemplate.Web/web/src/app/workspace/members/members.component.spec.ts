import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { WorkspaceMembersComponent } from './members.component';
import { UsersApiService } from '../../shared/api/users-api.service';
import { WorkspaceApiService } from '../../shared/api/workspace-api.service';
import { InvitationApiService } from '../../shared/api/invitation-api.service';
import { UserService } from '../../shared/core/user.service';
import { PermissionEnum } from '../../shared/enums/permission.enum';
import { InvitationStatusEnum } from '../../shared/enums/invitation-status.enum';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';

describe('WorkspaceMembersComponent', () => {
  let fixture: ComponentFixture<WorkspaceMembersComponent>;
  let el: HTMLElement;
  let usersApi: jasmine.SpyObj<UsersApiService>;
  let workspaceApi: jasmine.SpyObj<WorkspaceApiService>;
  let invitationApi: jasmine.SpyObj<InvitationApiService>;

  async function render(own: PermissionEnum[]) {
    usersApi = jasmine.createSpyObj<UsersApiService>('UsersApiService', ['getProfiles', 'getUserRoles', 'setUserRoles']);
    usersApi.getProfiles.and.resolveTo([
      { id: 'me', userName: 'Oleg', avatarId: null },
      { id: 'u-vasya', userName: 'Vasya', avatarId: null }
    ]);
    workspaceApi = jasmine.createSpyObj<WorkspaceApiService>('WorkspaceApiService', ['removeMember']);
    workspaceApi.removeMember.and.resolveTo();
    invitationApi = jasmine.createSpyObj<InvitationApiService>('InvitationApiService', ['getList', 'revoke', 'link', 'create']);
    invitationApi.getList.and.resolveTo([{
      id: 'inv-1', token: 't-1', inviteeName: 'Валера', status: InvitationStatusEnum.Active,
      createdAtUtc: '2026-10-30T10:00:00Z', expiresAtUtc: '2026-11-02T10:00:00Z',
      acceptedByUserName: null, permissions: [PermissionEnum.WorkspaceMemberRead]
    }]);

    await TestBed.configureTestingModule({
      imports: [WorkspaceMembersComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: UsersApiService, useValue: usersApi },
        { provide: WorkspaceApiService, useValue: workspaceApi },
        { provide: InvitationApiService, useValue: invitationApi }
      ]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));
    TestBed.inject(UserService).applySession({
      token: 'token',
      profile: { id: 'me', userName: 'Oleg', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true },
      permissionsByWorkspace: { 'ws-1': own }
    });

    fixture = TestBed.createComponent(WorkspaceMembersComponent);
    el = fixture.nativeElement;
    await fixture.whenStable();
  }

  const rows = () => Array.from(el.querySelectorAll('.member-row'));

  // Список отдельным правом не закрыт: его видит всякий, кто попал на страницу
  it('should list members and mark the current user', async () => {
    await render([PermissionEnum.WorkspaceMemberRead]);

    expect(rows().map(r => r.querySelector('.fw-semibold')?.textContent?.trim())).toEqual(['Oleg', 'Vasya']);
    expect(rows()[0].textContent).toContain('вы');
  });

  it('should hide access editing, removal and invitations from a reader', async () => {
    await render([PermissionEnum.WorkspaceMemberRead]);

    expect(el.querySelector('.edit-access')).toBeNull();
    expect(el.querySelector('.kick')).toBeNull();
    expect(el.querySelector('workspace-invite-form')).toBeNull();
    expect(invitationApi.getList).not.toHaveBeenCalled();
  });

  // Себя не выгоняют — для этого есть «Выйти» в списке пространств
  it('should let a permission manager remove others but not themselves', async () => {
    await render([PermissionEnum.PermissionManagement]);

    const [self, vasya] = rows();
    expect(self.querySelector('.kick')).toBeNull();
    expect(vasya.querySelector('.kick')).not.toBeNull();

    vasya.querySelector<HTMLButtonElement>('.kick')!.click();
    await fixture.whenStable();
    vasya.querySelector<HTMLButtonElement>('.kick-confirm')!.click();
    await fixture.whenStable();

    expect(workspaceApi.removeMember).toHaveBeenCalledWith('u-vasya', 'ws-1');
  });

  it('should list issued invitations to whoever may invite', async () => {
    await render([PermissionEnum.WorkspaceMemberCrud]);

    expect(invitationApi.getList).toHaveBeenCalledWith('ws-1');
    expect(el.querySelector('.invitation-row')?.textContent).toContain('Валера');
    expect(el.querySelector('.revoke-invitation')).not.toBeNull();
  });
});
