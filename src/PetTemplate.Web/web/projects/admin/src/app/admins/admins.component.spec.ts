import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AdminsComponent } from './admins.component';
import { AdminRole } from '../core/admin-role';
import { AdminInvitationStatus } from '../core/admin-invitation-status';
import { AdminInvitation, AdminMember } from '../core/admin.model';
import { AdminSessionService } from '../core/admin-session.service';

/** Ответ HTTP разворачивается цепочкой промисов компонента — даём ей дойти и перерисоваться */
async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  await fixture.whenStable();
  fixture.detectChanges();
}

describe('AdminsComponent', () => {
  let httpMock: HttpTestingController;

  const admins: AdminMember[] = [
    {
      userId: 'u-1',
      userName: 'Oleg',
      roles: [
        { role: AdminRole.AdminManagement, grantedUtc: '2026-09-01T00:00:00Z', grantedByName: null },
        { role: AdminRole.Accounts, grantedUtc: '2026-09-01T00:00:00Z', grantedByName: null }
      ]
    },
    {
      userId: 'u-2',
      userName: 'Vasya',
      roles: [{ role: AdminRole.Accounts, grantedUtc: '2026-09-20T00:00:00Z', grantedByName: 'Oleg' }]
    }
  ];

  const pending: AdminInvitation = {
    id: 'i-1',
    roles: [AdminRole.Accounts],
    status: AdminInvitationStatus.Pending,
    createdUtc: '2026-09-29T12:00:00Z',
    expiresUtc: '2026-10-02T12:00:00Z',
    createdByName: 'Oleg',
    acceptedByName: null,
    revokedByName: null
  };

  const accepted: AdminInvitation = {
    ...pending,
    id: 'i-2',
    status: AdminInvitationStatus.Accepted,
    acceptedByName: 'Vasya'
  };

  const revoked: AdminInvitation = {
    ...pending,
    id: 'i-3',
    status: AdminInvitationStatus.Revoked,
    revokedByName: 'Oleg'
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AdminsComponent],
      providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    });
    httpMock = TestBed.inject(HttpTestingController);
    TestBed.inject(AdminSessionService).admin.set({
      userId: 'u-1',
      userName: 'Oleg',
      roles: [AdminRole.AdminManagement, AdminRole.Accounts]
    });
  });

  afterEach(() => httpMock.verify());

  async function render() {
    const fixture = TestBed.createComponent(AdminsComponent);
    fixture.detectChanges();
    httpMock.expectOne('/api/Admin/Admins').flush(admins);
    httpMock.expectOne('/api/Admin/Invitations').flush([pending, accepted, revoked]);
    await settle(fixture);
    // Оба списка приходят через Promise.all — ему нужен ещё один оборот
    await settle(fixture);
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  function button(element: HTMLElement, text: string): HTMLButtonElement {
    return Array.from(element.querySelectorAll('button')).find(x => x.textContent!.trim() === text)!;
  }

  it('should show admins with their roles', async () => {
    const { element } = await render();

    const rows = element.querySelectorAll('.admin-row');
    expect(rows[0].textContent).toContain('Oleg');
    expect(rows[0].textContent).toContain('Управление админами');
    expect(rows[1].textContent).toContain('Аккаунты');
  });

  it('should not offer to revoke the last role', async () => {
    const { element } = await render();

    const rows = element.querySelectorAll('.admin-row');
    expect(rows[0].querySelectorAll('.revoke-role').length).toBe(2);
    expect(rows[1].querySelectorAll('.revoke-role').length).toBe(0);
  });

  it('should offer to grant only own roles the admin lacks', async () => {
    const { element } = await render();

    const options = Array.from(element.querySelectorAll('.admin-row')[1].querySelectorAll('.grant-role option'))
      .map(x => x.textContent!.trim());
    expect(options).toEqual(['—', 'Управление админами']);
  });

  it('should ask for confirmation before removing an admin', async () => {
    const { fixture, element } = await render();

    element.querySelectorAll('.admin-row')[1].querySelector<HTMLButtonElement>('.btn-outline-danger')!.click();
    await settle(fixture);
    httpMock.expectNone('/api/Admin/Admins/u-2');

    button(element, 'Да, убрать').click();
    const req = httpMock.expectOne('/api/Admin/Admins/u-2');
    expect(req.request.method).toBe('DELETE');
  });

  it('should offer only own roles in the invitation form', async () => {
    const { element } = await render();

    const labels = Array.from(element.querySelectorAll('.invite-form label')).map(x => x.textContent!.trim());
    expect(labels).toEqual(['Управление админами', 'Аккаунты']);
  });

  it('should issue an invitation link with picked roles', async () => {
    const { fixture, element } = await render();
    element.querySelector<HTMLInputElement>('#invite-role-1')!.click();
    await settle(fixture);

    element.querySelector<HTMLFormElement>('.invite-form')!.dispatchEvent(new Event('submit'));
    const req = httpMock.expectOne('/api/Admin/Invitations');

    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ roles: [AdminRole.AdminManagement] });
    req.flush({ ...pending, id: 'i-3', token: 'tok-3' });
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Invitations').flush([pending, accepted, revoked]);
    await settle(fixture);
    expect(element.querySelector('.issued')!.textContent).toContain('/invite/tok-3');
  });

  it('should show statuses and let revoke only pending invitations', async () => {
    const { element } = await render();

    const rows = element.querySelectorAll('.invitation-row');
    expect(rows[0].textContent).toContain('ждёт');
    expect(rows[0].querySelector('.revoke-invitation')).not.toBeNull();
    expect(rows[1].textContent).toContain('принято · Vasya');
    expect(rows[1].querySelector('.revoke-invitation')).toBeNull();
  });

  it('should show who revoked an invitation', async () => {
    const { element } = await render();

    expect(element.querySelectorAll('.invitation-row')[2].textContent).toContain('отозвано · Oleg');
  });

  it('should not offer to copy a link from the list', async () => {
    const { element } = await render();

    expect(element.querySelector('.invitation-row')!.textContent).not.toContain('Ссылка');
  });

  /** После действия оба списка перечитываются заново — через тот же Promise.all */
  async function reloaded(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Admins').flush(admins);
    httpMock.expectOne('/api/Admin/Invitations').flush([pending, accepted, revoked]);
    await settle(fixture);
    await settle(fixture);
  }

  it('should grant the picked role and reread the list', async () => {
    const { fixture, element } = await render();

    const select = element.querySelectorAll('.admin-row')[1].querySelector<HTMLSelectElement>('.grant-role')!;
    select.value = String(AdminRole.AdminManagement);
    select.dispatchEvent(new Event('change'));
    const req = httpMock.expectOne(`/api/Admin/Admins/u-2/Roles/${AdminRole.AdminManagement}`);

    expect(req.request.method).toBe('POST');
    expect(select.value).toBe('');
    req.flush(null);
    await reloaded(fixture);
  });

  it('should revoke a role by its cross', async () => {
    const { fixture, element } = await render();

    element.querySelectorAll('.admin-row')[0].querySelector<HTMLButtonElement>('.revoke-role')!.click();
    const req = httpMock.expectOne(`/api/Admin/Admins/u-1/Roles/${AdminRole.AdminManagement}`);

    expect(req.request.method).toBe('DELETE');
    req.flush(null);
    await reloaded(fixture);
  });

  it('should show the server refusal as is and keep the list', async () => {
    const { fixture, element } = await render();

    element.querySelectorAll('.admin-row')[0].querySelectorAll<HTMLButtonElement>('.revoke-role')[1].click();
    httpMock.expectOne(`/api/Admin/Admins/u-1/Roles/${AdminRole.Accounts}`).flush(
      { error: 'Это последняя роль: без ролей человек не админ — удалите его явно' },
      { status: 400, statusText: 'Bad Request' });
    await settle(fixture);

    expect(element.querySelector('.alert-danger')!.textContent).toContain('Это последняя роль');
    expect(element.querySelectorAll('.admin-row').length).toBe(2);
    expect(element.querySelectorAll('.admin-row')[0].querySelectorAll<HTMLButtonElement>('.revoke-role')[1].disabled).toBeFalse();
  });

  it('should not remove an admin when the confirmation is cancelled', async () => {
    const { fixture, element } = await render();

    element.querySelectorAll('.admin-row')[1].querySelector<HTMLButtonElement>('.btn-outline-danger')!.click();
    await settle(fixture);
    button(element, 'Отмена').click();
    await settle(fixture);

    expect(element.querySelector('.confirmation')).toBeNull();
    httpMock.expectNone('/api/Admin/Admins/u-2');
  });

  it('should not offer to issue a link without roles', async () => {
    const { element } = await render();

    expect(button(element, 'Выдать ссылку').disabled).toBeTrue();
  });

  it('should hide the issued link once it is revoked', async () => {
    const { fixture, element } = await render();
    element.querySelector<HTMLInputElement>('#invite-role-1')!.click();
    await settle(fixture);
    element.querySelector<HTMLFormElement>('.invite-form')!.dispatchEvent(new Event('submit'));
    const fresh = { ...pending, id: 'i-9' };
    httpMock.expectOne('/api/Admin/Invitations').flush({ ...fresh, token: 'tok-9' });
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Invitations').flush([fresh, accepted]);
    await settle(fixture);
    await settle(fixture);
    expect(element.querySelector('.issued')).not.toBeNull();

    element.querySelector<HTMLButtonElement>('.invitation-row .revoke-invitation')!.click();
    const req = httpMock.expectOne('/api/Admin/Invitations/i-9');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Invitations').flush([{ ...fresh, status: AdminInvitationStatus.Revoked, revokedByName: 'Oleg' }, accepted]);
    await settle(fixture);

    expect(element.querySelector('.issued')).toBeNull();
    expect(element.querySelector('.invitation-row')!.textContent).toContain('отозвано · Oleg');
  });
});
