import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { InviteComponent } from './invite.component';
import { AdminRole } from '../core/admin-role';
import { AdminInvitationStatus } from '../core/admin-invitation-status';
import { AdminSessionService } from '../core/admin-session.service';

/** Ответ HTTP разворачивается цепочкой промисов компонента — даём ей дойти и перерисоваться */
async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  await fixture.whenStable();
  fixture.detectChanges();
}

describe('InviteComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [InviteComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ token: 'tok-1' }) } } }
      ]
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  async function render(respond: (req: ReturnType<HttpTestingController['expectOne']>) => void) {
    const fixture = TestBed.createComponent(InviteComponent);
    fixture.detectChanges();
    respond(httpMock.expectOne('/api/Admin/Invitations/tok-1'));
    await settle(fixture);
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  async function type(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }, input: HTMLInputElement, value: string) {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle(fixture);
  }

  it('should show who invites and with which roles', async () => {
    const { element } = await render(req => req.flush({
      status: AdminInvitationStatus.Pending,
      inviterName: 'Oleg',
      roles: [AdminRole.AdminManagement, AdminRole.Accounts]
    }));

    expect(element.querySelector('.invitation')!.textContent).toContain('Oleg');
    expect(element.querySelector('.invitation')!.textContent).toContain('Управление админами, Аккаунты');
    expect(element.textContent).toContain('сначала заведите его на');
  });

  it('should not offer the form for a revoked invitation', async () => {
    const { element } = await render(req => req.flush({ status: AdminInvitationStatus.Revoked, inviterName: null, roles: [] }));

    expect(element.querySelector('form')).toBeNull();
    expect(element.querySelector('.dead')!.textContent).toContain('отозвано');
  });

  it('should say so for an unknown token', async () => {
    const { element } = await render(req => req.flush(null, { status: 404, statusText: 'Not Found' }));

    expect(element.querySelector('form')).toBeNull();
    expect(element.querySelector('.dead')!.textContent).toContain('Такого приглашения нет');
  });

  it('should accept with product login and start the session', async () => {
    const { fixture, element } = await render(req => req.flush({
      status: AdminInvitationStatus.Pending,
      inviterName: 'Oleg',
      roles: [AdminRole.Accounts]
    }));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await type(fixture, element.querySelector<HTMLInputElement>('input[name=login]')!, 'Vasya');
    await type(fixture, element.querySelector<HTMLInputElement>('input[name=password]')!, 'secret');

    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    const req = httpMock.expectOne('/api/Admin/Invitations/tok-1/Accept');
    expect(req.request.body).toEqual({ login: 'Vasya', password: 'secret' });
    req.flush({
      token: 'admin-token',
      expiresUtc: new Date(Date.now() + 3_600_000).toISOString(),
      admin: { userId: 'u-2', userName: 'Vasya', roles: [AdminRole.Accounts] }
    });
    await settle(fixture);

    expect(TestBed.inject(AdminSessionService).admin()!.userName).toBe('Vasya');
    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('should say a used link is one-time', async () => {
    const { element } = await render(req => req.flush({ status: AdminInvitationStatus.Accepted, inviterName: null, roles: [] }));

    expect(element.querySelector('form')).toBeNull();
    expect(element.querySelector('.dead')!.textContent).toContain('одноразовая');
  });

  it('should say an expired invitation is over', async () => {
    const { element } = await render(req => req.flush({ status: AdminInvitationStatus.Expired, inviterName: null, roles: [] }));

    expect(element.querySelector('.dead')!.textContent).toContain('Срок приглашения вышел');
  });

  it('should call the inviter just an admin when there is no nick', async () => {
    const { element } = await render(req => req.flush({
      status: AdminInvitationStatus.Pending,
      inviterName: null,
      roles: [AdminRole.Accounts]
    }));

    expect(element.querySelector('.invitation b')!.textContent).toBe('Админ');
  });

  it('should explain a refused acceptance without starting the session', async () => {
    const { fixture, element } = await render(req => req.flush({
      status: AdminInvitationStatus.Pending,
      inviterName: 'Oleg',
      roles: [AdminRole.Accounts]
    }));
    await type(fixture, element.querySelector<HTMLInputElement>('input[name=login]')!, 'Vasya');
    await type(fixture, element.querySelector<HTMLInputElement>('input[name=password]')!, 'wrong');

    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    httpMock.expectOne('/api/Admin/Invitations/tok-1/Accept').flush(null, { status: 404, statusText: 'Not Found' });
    await settle(fixture);

    expect(element.querySelector('.alert-danger')!.textContent)
      .toContain('неверный логин или пароль, слишком много попыток или ссылка уже не действует');
    expect(TestBed.inject(AdminSessionService).token).toBeNull();
    expect(element.querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(false);
  });
});
