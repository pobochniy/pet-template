import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideExperimentalZonelessChangeDetection } from '@angular/core';
import { Router } from '@angular/router';
import { TopNavComponent } from './top-nav.component';
import { EventEmitterService } from '../event-emitter.service';
import { UserService } from '../../core/user.service';
import { AuthApiService } from '../../api/auth-api.service';
import { ChatService } from '../../chat/chat.service';
import { OrganizationContextService } from '../../core/organization-context.service';
import { OrganizationApiService } from '../../api/organization-api.service';
import { OrganizationModel } from '../../models/organization.model';
import { UserModel } from '../../models/user.model';
import { PermissionEnum } from '../../enums/permission.enum';

describe('TopNavComponent - Organization Selector', () => {
  let component: TopNavComponent;
  let fixture: ComponentFixture<TopNavComponent>;
  let mockUserService: jasmine.SpyObj<UserService>;
  let mockOrganizationApiService: jasmine.SpyObj<OrganizationApiService>;
  let mockOrganizationContextService: jasmine.SpyObj<OrganizationContextService>;
  let mockAuthService: jasmine.SpyObj<AuthApiService>;
  let mockRouter: jasmine.SpyObj<Router>;
  let mockChatService: jasmine.SpyObj<ChatService>;
  let mockEventEmitterService: jasmine.SpyObj<EventEmitterService>;

  const createMockOrganization = (id: string, name: string): OrganizationModel => ({
    id,
    name,
    inn: '1234567890',
    kpp: '123456789',
    description: `Description for ${name}`,
    created: new Date(),
    founderId: 'founder-1',
    isActive: true
  });

  const createMockUser = (orgIds: string[]): UserModel => {
    const user = new UserModel();
    user.id = 'user-1';
    user.userName = 'testUser';
    user.email = 'test@example.com';
    user.permissionsByOrganization = {};

    orgIds.forEach(orgId => {
      user.permissionsByOrganization![orgId] = [PermissionEnum.organizationCrud];
    });

    return user;
  };

  beforeEach(async () => {
    mockUserService = jasmine.createSpyObj('UserService', ['getOrganizationIds'], {
      isAuth: true
    });

    // Create spy for User property setter
    let userValue: any = createMockUser(['1', '2']);
    Object.defineProperty(mockUserService, 'User', {
      get: jasmine.createSpy('User getter').and.callFake(() => userValue),
      set: jasmine.createSpy('User setter').and.callFake((val) => { userValue = val; }),
      enumerable: true,
      configurable: true
    });

    mockOrganizationApiService = jasmine.createSpyObj('OrganizationApiService', ['getList']);
    mockOrganizationContextService = jasmine.createSpyObj('OrganizationContextService', [
      'getCurrentOrganizationId',
      'setCurrentOrganizationId',
      'clearCurrentOrganization'
    ]);
    mockAuthService = jasmine.createSpyObj('AuthApiService', ['logOut']);
    mockRouter = jasmine.createSpyObj('Router', ['navigateByUrl']);
    mockChatService = jasmine.createSpyObj('ChatService', ['disconnect']);
    mockEventEmitterService = jasmine.createSpyObj('EventEmitterService', ['onToggleMenuButtonClick']);

    await TestBed.configureTestingModule({
      imports: [TopNavComponent],
      providers: [
        provideExperimentalZonelessChangeDetection(),
        { provide: UserService, useValue: mockUserService },
        { provide: OrganizationApiService, useValue: mockOrganizationApiService },
        { provide: OrganizationContextService, useValue: mockOrganizationContextService },
        { provide: AuthApiService, useValue: mockAuthService },
        { provide: Router, useValue: mockRouter },
        { provide: ChatService, useValue: mockChatService },
        { provide: EventEmitterService, useValue: mockEventEmitterService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(TopNavComponent);
    component = fixture.componentInstance;
  });

  describe('Scenario 1: User has no organizations', () => {
    beforeEach(() => {
      mockUserService.getOrganizationIds.and.returnValue([]);
      mockOrganizationApiService.getList.and.returnValue(Promise.resolve([]));
    });

    it('should hide organization selector when user has no organizations', async () => {
      await component.ngOnInit();

      expect(component.showOrganizationSelector()).toBe(false);
      expect(component.availableOrganizations()).toEqual([]);
    });

    it('should not call organization API when user has no organization IDs', async () => {
      await component.ngOnInit();

      expect(mockOrganizationApiService.getList).not.toHaveBeenCalled();
    });
  });

  describe('Scenario 2: User has exactly one organization', () => {
    const org1 = createMockOrganization('1', 'Organization One');

    beforeEach(() => {
      mockUserService.getOrganizationIds.and.returnValue(['1']);
      mockOrganizationApiService.getList.and.returnValue(Promise.resolve([org1]));
      mockOrganizationContextService.getCurrentOrganizationId.and.returnValue(null);
    });

    it('should show organization selector', async () => {
      await component.ngOnInit();

      expect(component.showOrganizationSelector()).toBe(true);
    });

    it('should disable organization selector when only one organization', async () => {
      await component.ngOnInit();

      expect(component.isOrganizationSelectorDisabled()).toBe(true);
    });

    it('should auto-select the only available organization', async () => {
      await component.ngOnInit();

      expect(component.selectedOrganizationId()).toBe('1');
      expect(mockOrganizationContextService.setCurrentOrganizationId).toHaveBeenCalledWith('1');
    });

    it('should set available organizations to filtered list', async () => {
      await component.ngOnInit();

      expect(component.availableOrganizations()).toEqual([org1]);
    });
  });

  describe('Scenario 3: User has multiple organizations', () => {
    const org1 = createMockOrganization('1', 'Organization One');
    const org2 = createMockOrganization('2', 'Organization Two');
    const org3 = createMockOrganization('3', 'Organization Three');

    beforeEach(() => {
      mockUserService.getOrganizationIds.and.returnValue(['1', '2']);
      mockOrganizationApiService.getList.and.returnValue(Promise.resolve([org1, org2, org3]));
      mockOrganizationContextService.getCurrentOrganizationId.and.returnValue(null);
    });

    it('should show organization selector', async () => {
      await component.ngOnInit();

      expect(component.showOrganizationSelector()).toBe(true);
    });

    it('should enable organization selector when multiple organizations', async () => {
      await component.ngOnInit();

      expect(component.isOrganizationSelectorDisabled()).toBe(false);
    });

    it('should filter organizations based on user permissions', async () => {
      await component.ngOnInit();

      expect(component.availableOrganizations().length).toBe(2);
      expect(component.availableOrganizations()).toContain(org1);
      expect(component.availableOrganizations()).toContain(org2);
      expect(component.availableOrganizations()).not.toContain(org3);
    });

    it('should auto-select first organization when no saved preference', async () => {
      await component.ngOnInit();

      expect(component.selectedOrganizationId()).toBe('1');
      expect(mockOrganizationContextService.setCurrentOrganizationId).toHaveBeenCalledWith('1');
    });
  });

  describe('Scenario 4: Saved organization preference', () => {
    const org1 = createMockOrganization('1', 'Organization One');
    const org2 = createMockOrganization('2', 'Organization Two');

    beforeEach(() => {
      mockUserService.getOrganizationIds.and.returnValue(['1', '2']);
      mockOrganizationApiService.getList.and.returnValue(Promise.resolve([org1, org2]));
    });

    it('should restore saved organization from localStorage', async () => {
      mockOrganizationContextService.getCurrentOrganizationId.and.returnValue('2');

      await component.ngOnInit();

      expect(component.selectedOrganizationId()).toBe('2');
      expect(mockOrganizationContextService.setCurrentOrganizationId).not.toHaveBeenCalled();
    });

    it('should fallback to first organization if saved org is not in available list', async () => {
      mockOrganizationContextService.getCurrentOrganizationId.and.returnValue('999');

      await component.ngOnInit();

      expect(component.selectedOrganizationId()).toBe('1');
      expect(mockOrganizationContextService.setCurrentOrganizationId).toHaveBeenCalledWith('1');
    });
  });

  describe('Organization change handling', () => {
    it('should update context service when organization is changed', () => {
      component.selectedOrganizationId.set('2');

      component.onOrganizationChange();

      expect(mockOrganizationContextService.setCurrentOrganizationId).toHaveBeenCalledWith('2');
    });

    it('should not update context service when selectedOrganizationId is null', () => {
      component.selectedOrganizationId.set(null);

      component.onOrganizationChange();

      expect(mockOrganizationContextService.setCurrentOrganizationId).not.toHaveBeenCalled();
    });
  });

  describe('Logout behavior', () => {
    it('should clear current organization on logout', async () => {
      mockAuthService.logOut.and.returnValue(Promise.resolve());
      mockRouter.navigateByUrl.and.returnValue(Promise.resolve(true));

      await component.logOut();

      expect(mockOrganizationContextService.clearCurrentOrganization).toHaveBeenCalled();
    });

    it('should clear user and disconnect chat on logout', async () => {
      mockAuthService.logOut.and.returnValue(Promise.resolve());
      mockRouter.navigateByUrl.and.returnValue(Promise.resolve(true));

      await component.logOut();

      const userSetterSpy = Object.getOwnPropertyDescriptor(mockUserService, 'User')?.set as jasmine.Spy;
      expect(userSetterSpy).toHaveBeenCalledWith(undefined);
      expect(mockAuthService.logOut).toHaveBeenCalled();
      expect(mockChatService.disconnect).toHaveBeenCalled();
      expect(mockRouter.navigateByUrl).toHaveBeenCalledWith('/');
    });
  });

  describe('Edge cases', () => {
    it('should handle null response from organization API', async () => {
      mockUserService.getOrganizationIds.and.returnValue(['1']);
      mockOrganizationApiService.getList.and.returnValue(Promise.resolve(null as any));

      await component.ngOnInit();

      expect(component.showOrganizationSelector()).toBe(false);
      expect(component.availableOrganizations()).toEqual([]);
    });

    it('should handle undefined response from organization API', async () => {
      mockUserService.getOrganizationIds.and.returnValue(['1']);
      mockOrganizationApiService.getList.and.returnValue(Promise.resolve(undefined as any));

      await component.ngOnInit();

      expect(component.showOrganizationSelector()).toBe(false);
      expect(component.availableOrganizations()).toEqual([]);
    });

    it('should hide selector when user has org IDs but API returns no matching organizations', async () => {
      mockUserService.getOrganizationIds.and.returnValue(['999']);
      mockOrganizationApiService.getList.and.returnValue(
        Promise.resolve([createMockOrganization('1', 'Org One')])
      );

      await component.ngOnInit();

      expect(component.showOrganizationSelector()).toBe(false);
    });
  });
});
