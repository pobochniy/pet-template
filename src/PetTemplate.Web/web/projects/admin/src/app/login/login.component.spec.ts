import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { LoginComponent } from './login.component';
import { AdminSessionService } from '../core/admin-session.service';
import { AdminRole } from '../core/admin-role';

/** Ответ HTTP разворачивается цепочкой промисов компонента — даём ей дойти и перерисоваться */
async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  await fixture.whenStable();
  fixture.detectChanges();
}

/** Вход логином и паролем от продукта (спец. admin, 3.3); отказ сервера — один `404` на всё */
describe('LoginComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.inject(AdminSessionService).admin.set(null);
    localStorage.clear();
  });

  async function render() {
    const fixture = TestBed.createComponent(LoginComponent);
    await settle(fixture);
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  async function fill(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }, element: HTMLElement, login: string, password: string) {
    for (const [name, value] of [['login', login], ['password', password]]) {
      const input = element.querySelector<HTMLInputElement>(`input[name=${name}]`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    await settle(fixture);
  }

  function submitButton(element: HTMLElement): HTMLButtonElement {
    return element.querySelector<HTMLButtonElement>('button[type=submit]')!;
  }

  it('should not offer to sign in until both fields are filled', async () => {
    const { fixture, element } = await render();
    expect(submitButton(element).disabled).toBe(true);

    await fill(fixture, element, 'Oleg', '');
    expect(submitButton(element).disabled).toBe(true);

    await fill(fixture, element, 'Oleg', 'secret');
    expect(submitButton(element).disabled).toBe(false);
  });

  it('should start the session and open the shell', async () => {
    const { fixture, element } = await render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await fill(fixture, element, 'Oleg', 'secret');

    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    const req = httpMock.expectOne('/api/Admin/Login');
    expect(req.request.body).toEqual({ login: 'Oleg', password: 'secret' });
    req.flush({
      token: 'admin-token',
      expiresUtc: new Date(Date.now() + 3_600_000).toISOString(),
      admin: { userId: 'u-1', userName: 'Oleg', roles: [AdminRole.AdminManagement] }
    });
    await settle(fixture);

    expect(TestBed.inject(AdminSessionService).token).toBe('admin-token');
    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('should stay on the form and let retry when the server refuses', async () => {
    const { fixture, element } = await render();
    await fill(fixture, element, 'Oleg', 'wrong');

    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    httpMock.expectOne('/api/Admin/Login').flush(null, { status: 404, statusText: 'Not Found' });
    await settle(fixture);

    expect(element.querySelector('.alert-danger')!.textContent).toContain('Не вошли');
    expect(element.querySelector('.alert-danger')!.textContent).toContain('неверный логин или пароль');
    expect(element.querySelector('.alert-danger')!.textContent).not.toContain('404');
    expect(TestBed.inject(AdminSessionService).token).toBeNull();
    expect(submitButton(element).disabled).toBe(false);
  });
});
