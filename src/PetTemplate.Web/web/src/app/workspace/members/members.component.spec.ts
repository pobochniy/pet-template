import type { Mocked } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { WorkspaceMembersComponent } from './members.component';
import { UsersApiService } from '../users-api.service';
import { WorkspaceApiService } from '../../shared/api/workspace-api.service';
import { InvitationApiService } from '../../shared/api/invitation-api.service';
import { UserService } from '../../shared/core/user.service';
import { PermissionEnum } from '../../shared/enums/permission.enum';
import { InvitationStatusEnum } from '../../shared/enums/invitation-status.enum';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';
import { UserProfileModel } from '../../shared/models/user-profile.model';
import { profileDto, sessionDto } from '../../shared/api/dto.mock';

describe('WorkspaceMembersComponent', () => {
  let fixture: ComponentFixture<WorkspaceMembersComponent>;
  let el: HTMLElement;
  let usersApi: Mocked<UsersApiService>;
  let workspaceApi: Mocked<WorkspaceApiService>;
  let invitationApi: Mocked<InvitationApiService>;

  async function render(own: PermissionEnum[]) {
    usersApi = { getProfiles: vi.fn(), getUserRoles: vi.fn(), setUserRoles: vi.fn() } as unknown as Mocked<UsersApiService>;
    usersApi.getProfiles.mockResolvedValue([
      { id: 'me', userName: 'Oleg', avatarId: null },
      { id: 'u-vasya', userName: 'Vasya', avatarId: null }
    ]);
    workspaceApi = { removeMember: vi.fn() } as unknown as Mocked<WorkspaceApiService>;
    workspaceApi.removeMember.mockResolvedValue(undefined);
    invitationApi = { getList: vi.fn(), revoke: vi.fn(), link: vi.fn(), create: vi.fn() } as unknown as Mocked<InvitationApiService>;
    invitationApi.getList.mockResolvedValue([{
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
    TestBed.inject(UserService).applySession(sessionDto({
      token: 'token',
      profile: profileDto({ id: 'me', userName: 'Oleg', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true }),
      permissionsByWorkspace: { 'ws-1': own }
    }));

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

    expect(invitationApi.getList).toHaveBeenCalledWith('ws-1', expect.any(AbortSignal));
    expect(el.querySelector('.invitation-row')?.textContent).toContain('Валера');
    expect(el.querySelector('.revoke-invitation')).not.toBeNull();
  });

  // Сменили пространство в шапке, не уходя со страницы: прежний запрос обрывается,
  // и его ответ не ложится поверх состава нового пространства
  it('should drop the members answer of a workspace that is no longer active', async () => {
    await render([PermissionEnum.WorkspaceMemberRead]);
    const user = TestBed.inject(UserService);
    user.applySession(sessionDto({
      token: 'token',
      profile: profileDto({ id: 'me', userName: 'Oleg', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true }),
      permissionsByWorkspace: { 'ws-1': [PermissionEnum.WorkspaceMemberRead], 'ws-2': [PermissionEnum.WorkspaceMemberRead] }
    }), 'ws-1');

    let resolveStale!: (value: UserProfileModel[]) => void;
    let staleSignal: AbortSignal | undefined;
    usersApi.getProfiles.mockImplementation(signal => {
      staleSignal = signal;
      return new Promise(resolve => (resolveStale = resolve));
    });
    user.switchWorkspace('ws-2');
    TestBed.tick();

    usersApi.getProfiles.mockResolvedValue([
      { id: 'me', userName: 'Oleg', avatarId: null },
      { id: 'u-masha', userName: 'Masha', avatarId: null }
    ]);
    user.switchWorkspace('ws-1');
    TestBed.tick();
    expect(staleSignal?.aborted).toBe(true);

    resolveStale([{ id: 'u-petya', userName: 'Petya', avatarId: null }]);
    await fixture.whenStable();

    expect(rows().map(r => r.querySelector('.fw-semibold')?.textContent?.trim())).toEqual(['Oleg', 'Masha']);
  });
});
