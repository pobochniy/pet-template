import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { WorkspaceSettingsComponent } from './settings.component';
import { WorkspaceApiService } from '../../shared/api/workspace-api.service';
import { UsersApiService } from '../../shared/api/users-api.service';
import { InvitationApiService } from '../../shared/api/invitation-api.service';
import { UserService } from '../../shared/core/user.service';
import { WorkspaceContextService } from '../../shared/core/workspace-context.service';
import { PermissionsService } from '../../shared/services/permissions.service';
import { PermissionEnum } from '../../shared/enums/permission.enum';
import { ProfileModel } from '../../shared/models/profile.model';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';

describe('WorkspaceSettingsComponent', () => {
  let fixture: ComponentFixture<WorkspaceSettingsComponent>;
  let el: HTMLElement;
  let api: jasmine.SpyObj<WorkspaceApiService>;
  let router: Router;

  const profile: ProfileModel = {
    id: 'me', userName: 'Oleg', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true,
    primaryWorkspaceId: 'ws-1'
  };

  async function render(permissionsByWorkspace: Record<string, PermissionEnum[]>, names: Record<string, string>) {
    api = jasmine.createSpyObj<WorkspaceApiService>('WorkspaceApiService',
      ['getList', 'create', 'rename', 'setPrimary', 'leave', 'removeMember']);
    api.getList.and.resolveTo(Object.entries(names).map(([id, name]) => ({ id, name, membersCount: 2 })));

    const usersApi = jasmine.createSpyObj<UsersApiService>('UsersApiService', ['getProfiles']);
    usersApi.getProfiles.and.resolveTo([]);
    const invitationApi = jasmine.createSpyObj<InvitationApiService>('InvitationApiService', ['getList', 'link']);
    invitationApi.getList.and.resolveTo([]);

    await TestBed.configureTestingModule({
      imports: [WorkspaceSettingsComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: WorkspaceApiService, useValue: api },
        { provide: UsersApiService, useValue: usersApi },
        { provide: InvitationApiService, useValue: invitationApi }
      ]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));
    TestBed.inject(UserService).applySession({ token: 'token', profile, permissionsByWorkspace });
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);

    fixture = TestBed.createComponent(WorkspaceSettingsComponent);
    el = fixture.nativeElement;
    await fixture.whenStable();
  }

  const rows = () => Array.from(el.querySelectorAll('.workspace-row'));

  it('should open the creation form right away for a user without workspaces', async () => {
    await render({}, {});

    expect(el.querySelector('#no-workspaces')).not.toBeNull();
    expect(el.querySelector('#workspace-name')).not.toBeNull();
  });

  // После создания — новая сессия, созданное активно, переход к участникам (спец. authorization, 3, п. 5)
  it('should apply the new session and go to the new workspace members', async () => {
    await render({}, {});
    api.create.and.resolveTo({
      workspaceId: 'ws-new',
      authentication: {
        token: 'new-token',
        profile: { ...profile, primaryWorkspaceId: 'ws-new' },
        permissionsByWorkspace: { 'ws-new': [PermissionEnum.PermissionManagement, PermissionEnum.WorkspaceMemberCrud] }
      }
    });

    const input = el.querySelector<HTMLInputElement>('#workspace-name')!;
    input.value = 'Котики и Собачки';
    input.dispatchEvent(new Event('input'));
    el.querySelector<HTMLButtonElement>('#create-workspace')!.click();
    await fixture.whenStable();

    expect(api.create).toHaveBeenCalledWith('Котики и Собачки');
    expect(TestBed.inject(WorkspaceContextService).currentWorkspaceId()).toBe('ws-new');
    expect(TestBed.inject(PermissionsService).permissions()).toContain(PermissionEnum.WorkspaceMemberCrud);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/workspace/members');
  });

  // Права смотрим у самого пространства строки, а не у активного
  it('should offer renaming only where the user has WorkspaceInfoCrud', async () => {
    await render(
      { 'ws-1': [PermissionEnum.WorkspaceMemberRead], 'ws-2': [PermissionEnum.WorkspaceInfoCrud] },
      { 'ws-1': 'Рога и Копыта', 'ws-2': 'Пончики и Плюшки' });

    const [first, second] = rows();
    expect(first.querySelector('.rename')).toBeNull();
    expect(second.querySelector('.rename')).not.toBeNull();
    expect(first.querySelector('.primary-badge')).not.toBeNull();
  });

  it('should rename a workspace with its own header', async () => {
    await render({ 'ws-1': [PermissionEnum.WorkspaceInfoCrud], 'ws-2': [PermissionEnum.WorkspaceInfoCrud] },
      { 'ws-1': 'Рога и Копыта', 'ws-2': 'Пончики и Плюшки' });
    api.rename.and.resolveTo();

    rows()[1].querySelector<HTMLButtonElement>('.rename')!.click();
    await fixture.whenStable();
    const input = rows()[1].querySelector<HTMLInputElement>('input')!;
    input.value = 'Бублики и Сушки';
    input.dispatchEvent(new Event('input'));
    rows()[1].querySelector<HTMLButtonElement>('.rename-save')!.click();
    await fixture.whenStable();

    expect(api.rename).toHaveBeenCalledWith('ws-2', 'Бублики и Сушки');
    expect(rows()[1].querySelector('.workspace-name')?.textContent?.trim()).toBe('Бублики и Сушки');
  });

  // Выход из не активного — активное остаётся прежним, сессия новая
  it('should keep the active workspace when leaving another one', async () => {
    await render({ 'ws-1': [PermissionEnum.WorkspaceMemberRead], 'ws-2': [PermissionEnum.WorkspaceMemberRead] },
      { 'ws-1': 'Рога и Копыта', 'ws-2': 'Пончики и Плюшки' });
    TestBed.inject(UserService).switchWorkspace('ws-2');
    api.leave.and.resolveTo({ token: 'new-token', profile, permissionsByWorkspace: { 'ws-2': [PermissionEnum.WorkspaceMemberRead] } });
    api.getList.and.resolveTo([{ id: 'ws-2', name: 'Пончики и Плюшки', membersCount: 2 }]);

    rows()[0].querySelector<HTMLButtonElement>('.leave')!.click();
    await fixture.whenStable();
    rows()[0].querySelector<HTMLButtonElement>('.leave-confirm')!.click();
    await fixture.whenStable();

    expect(api.leave).toHaveBeenCalledWith('ws-1');
    expect(TestBed.inject(WorkspaceContextService).currentWorkspaceId()).toBe('ws-2');
    expect(TestBed.inject(UserService).workspaceIds()).toEqual(['ws-2']);
  });
});
