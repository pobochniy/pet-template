import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { InviteComponent } from './invite.component';
import { InvitationApiService } from '../shared/api/invitation-api.service';
import { AuthApiService } from '../shared/api/auth-api.service';
import { UserService } from '../shared/core/user.service';
import { InvitationStatusEnum } from '../shared/enums/invitation-status.enum';
import { I18nService } from '../shared/i18n/i18n.service';
import { loadAllScopes } from '../shared/i18n/i18n.testing';
import { ExternalLoginApiService } from '../shared/api/external-login-api.service';
import { ExternalLoginApiServiceMock } from '../shared/api/external-login-api.service.mock';

describe('InviteComponent', () => {
  let fixture: ComponentFixture<InviteComponent>;
  let el: HTMLElement;
  let api: jasmine.SpyObj<InvitationApiService>;
  let router: Router;

  async function render(status: InvitationStatusEnum, signedIn = false) {
    api = jasmine.createSpyObj<InvitationApiService>('InvitationApiService', ['preview', 'accept', 'register']);
    api.preview.and.resolveTo(status === InvitationStatusEnum.Active
      ? { status, workspaceName: 'Рога и Копыта', inviterName: 'Oleg', inviteeName: 'Валера' }
      : { status, workspaceName: null, inviterName: null, inviteeName: null });
    api.accept.and.resolveTo();
    api.register.and.resolveTo();

    await TestBed.configureTestingModule({
      imports: [InviteComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ExternalLoginApiService, useClass: ExternalLoginApiServiceMock },
        provideRouter([]),
        { provide: InvitationApiService, useValue: api },
        { provide: AuthApiService, useValue: jasmine.createSpyObj<AuthApiService>('AuthApiService', ['login']) }
      ]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));
    if (signedIn) {
      TestBed.inject(UserService).setProfile({
        id: 'me', userName: 'Vasya', avatarId: null, comment: null, isAdult: true, hasAcceptedTerms: true
      });
    }
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);

    fixture = TestBed.createComponent(InviteComponent);
    fixture.componentRef.setInput('token', 'token-1');
    el = fixture.nativeElement;
    await fixture.whenStable();
  }

  it('should greet by name and name the workspace', async () => {
    await render(InvitationStatusEnum.Active);

    expect(el.querySelector('#invite-greeting')?.textContent).toContain('Привет, Валера!');
    expect(el.querySelector('#invite-greeting')?.textContent).toContain('Oleg приглашает вас в пространство «Рога и Копыта»');
  });

  it('should show the refusal of a revoked link', async () => {
    await render(InvitationStatusEnum.Revoked);

    expect(el.querySelector('#invite-refusal')?.textContent?.trim()).toBe('Приглашение отозвано. Попросите новую ссылку');
  });

  it('should let a signed-in user accept with one button', async () => {
    await render(InvitationStatusEnum.Active, true);

    el.querySelector<HTMLButtonElement>('#invite-accept')!.click();
    await fixture.whenStable();

    expect(api.accept).toHaveBeenCalledWith('token-1');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('should register a guest by the link', async () => {
    await render(InvitationStatusEnum.Active);

    const set = (id: string, value: string) => {
      const input = el.querySelector<HTMLInputElement>(id)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    set('#invite-username', 'Maxim');
    set('#invite-password', 'password123');
    set('#invite-password-confirm', 'password123');
    el.querySelector<HTMLButtonElement>('#invite-register-submit')!.click();
    await fixture.whenStable();

    expect(api.register).toHaveBeenCalledWith('token-1', 'Maxim', 'password123', 'password123', 'ru');
  });
});
