import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideExperimentalZonelessChangeDetection } from '@angular/core';
import { TopNavComponent } from './top-nav.component';
import { EventEmitterService } from '../event-emitter.service';
import { UserService } from '../../core/user.service';
import { AuthApiService } from '../../api/auth-api.service';
import { ChatService } from '../../chat/chat.service';
import { OrganizationContextService } from '../../core/organization-context.service';
import { OrganizationApiService } from '../../api/organization-api.service';
import { Router } from '@angular/router';
import { OrganizationModel } from '../../models/organization.model';
import { UserModel } from '../../models/user.model';
import { PermissionEnum } from '../../enums/permission.enum';
import { DebugElement } from '@angular/core';
import { By } from '@angular/platform-browser';

describe('TopNavComponent - Integration Tests', () => {
  let component: TopNavComponent;
  let fixture: ComponentFixture<TopNavComponent>;
  let compiled: HTMLElement;
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
      isAuth: true,
      User: createMockUser(['1', '2'])
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
    compiled = fixture.nativeElement;
  });

  describe('DOM Rendering - No Organizations', () => {
    beforeEach(async () => {
      mockUserService.getOrganizationIds.and.returnValue([]);
      await component.ngOnInit();
      fixture.detectChanges();
    });

    it('should not render organization selector in DOM', () => {
      const selector = compiled.querySelector('.organization-selector');
      expect(selector).toBeNull();
    });

    it('should still render profile and logout buttons', () => {
      const buttons = compiled.querySelectorAll('.icon-button');
      expect(buttons.length).toBe(2);
    });
  });

  describe('DOM Rendering - Single Organization', () => {
    const org1 = createMockOrganization('1', 'Single Org');

    beforeEach(async () => {
      mockUserService.getOrganizationIds.and.returnValue(['1']);
      mockOrganizationApiService.getList.and.returnValue(Promise.resolve([org1]));
      mockOrganizationContextService.getCurrentOrganizationId.and.returnValue(null);

      await component.ngOnInit();
      fixture.detectChanges();
    });

    it('should render organization selector in DOM', () => {
      const selector = compiled.querySelector('.organization-selector');
      expect(selector).toBeTruthy();
    });

    it('should have disabled attribute on select element', () => {
      const selector = compiled.querySelector('.organization-selector') as HTMLSelectElement;
      expect(selector.disabled).toBe(true);
    });

    it('should display organization name in option', () => {
      const options = compiled.querySelectorAll('.organization-selector option');
      expect(options.length).toBe(1);
      expect(options[0].textContent?.trim()).toBe('Single Org');
    });

    it('should have correct value selected', () => {
      const selector = compiled.querySelector('.organization-selector') as HTMLSelectElement;
      expect(selector.value).toBe('1');
    });
  });

  describe('DOM Rendering - Multiple Organizations', () => {
    const org1 = createMockOrganization('1', 'Organization Alpha');
    const org2 = createMockOrganization('2', 'Organization Beta');
    const org3 = createMockOrganization('3', 'Organization Gamma');

    beforeEach(async () => {
      mockUserService.getOrganizationIds.and.returnValue(['1', '2']);
      mockOrganizationApiService.getList.and.returnValue(Promise.resolve([org1, org2, org3]));
      mockOrganizationContextService.getCurrentOrganizationId.and.returnValue(null);

      await component.ngOnInit();
      fixture.detectChanges();
    });

    it('should render organization selector in DOM', () => {
      const selector = compiled.querySelector('.organization-selector');
      expect(selector).toBeTruthy();
    });

    it('should not have disabled attribute on select element', () => {
      const selector = compiled.querySelector('.organization-selector') as HTMLSelectElement;
      expect(selector.disabled).toBe(false);
    });

    it('should render only organizations user has access to', () => {
      const options = compiled.querySelectorAll('.organization-selector option');
      expect(options.length).toBe(2);

      const optionTexts = Array.from(options).map(opt => opt.textContent?.trim());
      expect(optionTexts).toContain('Organization Alpha');
      expect(optionTexts).toContain('Organization Beta');
      expect(optionTexts).not.toContain('Organization Gamma');
    });

    it('should have correct option values', () => {
      const options = compiled.querySelectorAll('.organization-selector option');
      const optionValues = Array.from(options).map(opt => (opt as HTMLOptionElement).value);

      expect(optionValues).toContain('1');
      expect(optionValues).toContain('2');
    });
  });

  describe('User Interactions', () => {
    const org1 = createMockOrganization('1', 'Org One');
    const org2 = createMockOrganization('2', 'Org Two');

    beforeEach(async () => {
      mockUserService.getOrganizationIds.and.returnValue(['1', '2']);
      mockOrganizationApiService.getList.and.returnValue(Promise.resolve([org1, org2]));
      mockOrganizationContextService.getCurrentOrganizationId.and.returnValue('1');

      await component.ngOnInit();
      fixture.detectChanges();
    });

    it('should call onOrganizationChange when user changes selection', () => {
      spyOn(component, 'onOrganizationChange');

      const selector = compiled.querySelector('.organization-selector') as HTMLSelectElement;
      selector.value = '2';
      selector.dispatchEvent(new Event('change'));
      selector.dispatchEvent(new Event('ngModelChange'));

      fixture.detectChanges();

      expect(component.onOrganizationChange).toHaveBeenCalled();
    });

    it('should update context service when organization changes', async () => {
      const selector = compiled.querySelector('.organization-selector') as HTMLSelectElement;

      component.selectedOrganizationId.set('2');
      component.onOrganizationChange();

      expect(mockOrganizationContextService.setCurrentOrganizationId).toHaveBeenCalledWith('2');
    });

    it('should navigate to profile when profile button is clicked', async () => {
      mockRouter.navigateByUrl.and.returnValue(Promise.resolve(true));

      const profileButton = Array.from(compiled.querySelectorAll('.icon-button'))
        .find(btn => btn.getAttribute('title') === 'Profile') as HTMLButtonElement;

      profileButton.click();
      await fixture.whenStable();

      expect(mockRouter.navigateByUrl).toHaveBeenCalledWith('/profiles');
    });

    it('should logout when logout button is clicked', async () => {
      mockAuthService.logOut.and.returnValue(Promise.resolve());
      mockRouter.navigateByUrl.and.returnValue(Promise.resolve(true));

      const logoutButton = Array.from(compiled.querySelectorAll('.icon-button'))
        .find(btn => btn.getAttribute('title') === 'Log Out') as HTMLButtonElement;

      logoutButton.click();
      await fixture.whenStable();

      expect(mockOrganizationContextService.clearCurrentOrganization).toHaveBeenCalled();
      expect(mockAuthService.logOut).toHaveBeenCalled();
    });
  });

  describe('CSS Classes and Styling', () => {
    const org1 = createMockOrganization('1', 'Org One');

    beforeEach(async () => {
      mockUserService.getOrganizationIds.and.returnValue(['1']);
      mockOrganizationApiService.getList.and.returnValue(Promise.resolve([org1]));
      mockOrganizationContextService.getCurrentOrganizationId.and.returnValue(null);

      await component.ngOnInit();
      fixture.detectChanges();
    });

    it('should have correct CSS class on organization selector', () => {
      const selector = compiled.querySelector('.organization-selector');
      expect(selector?.classList.contains('organization-selector')).toBe(true);
    });

    it('should have correct CSS class on icon buttons', () => {
      const buttons = compiled.querySelectorAll('.icon-button');
      buttons.forEach(button => {
        expect(button.classList.contains('icon-button')).toBe(true);
      });
    });

    it('should render navbar with correct class', () => {
      const navbar = compiled.querySelector('.top-navbar');
      expect(navbar).toBeTruthy();
    });

    it('should render navbar content container', () => {
      const content = compiled.querySelector('.top-navbar-content');
      expect(content).toBeTruthy();
    });
  });

  describe('Accessibility', () => {
    const org1 = createMockOrganization('1', 'Accessible Org');
    const org2 = createMockOrganization('2', 'Another Org');

    beforeEach(async () => {
      mockUserService.getOrganizationIds.and.returnValue(['1', '2']);
      mockOrganizationApiService.getList.and.returnValue(Promise.resolve([org1, org2]));
      mockOrganizationContextService.getCurrentOrganizationId.and.returnValue(null);

      await component.ngOnInit();
      fixture.detectChanges();
    });

    it('should have title attribute on profile button', () => {
      const profileButton = Array.from(compiled.querySelectorAll('.icon-button'))
        .find(btn => btn.getAttribute('title') === 'Profile');

      expect(profileButton?.getAttribute('title')).toBe('Profile');
    });

    it('should have title attribute on logout button', () => {
      const logoutButton = Array.from(compiled.querySelectorAll('.icon-button'))
        .find(btn => btn.getAttribute('title') === 'Log Out');

      expect(logoutButton?.getAttribute('title')).toBe('Log Out');
    });

    it('should have proper button type attributes', () => {
      const buttons = compiled.querySelectorAll('button');
      buttons.forEach(button => {
        expect(button.getAttribute('type')).toBe('button');
      });
    });
  });
});
