import { formatDate } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { AccountsComponent } from './accounts.component';
import { AdminAccountCard } from '../core/admin.model';

async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  await fixture.whenStable();
  fixture.detectChanges();
}

function card(overrides: Partial<AdminAccountCard['card']> = {}, isAdmin = false): AdminAccountCard {
  return {
    card: {
      userId: 'u-1', userName: 'masha', registeredAtUtc: '2026-01-10T10:00:00Z',
      hasPassword: true, hasTelegram: true, hasVk: false,
      canBeRemoved: true,
      activity: {},
      workspaces: [{ id: 'ws-1', name: 'Рога и Копыта', isOwner: true, otherMembers: ['petya'], isSolo: false }],
      ...overrides
    },
    email: 'masha@example.com', emailConfirmed: false, phoneNumber: '+79990000001',
    lastLoginAtUtc: '2026-10-01T08:00:00Z', isAdmin, actions: []
  };
}

const ISSUED = { url: 'https://example.com/auth/reset/abc', expiresAtUtc: '2026-10-08T10:00:00Z', replaced: false };

function type(fixture: { nativeElement: HTMLElement }, selector: string, value: string): void {
  const input = fixture.nativeElement.querySelector(selector) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('AccountsComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AccountsComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap({ id: 'u-1' })) } }
      ]
    });
    httpMock = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => httpMock.verify());

  async function render(answer: AdminAccountCard) {
    const fixture = TestBed.createComponent(AccountsComponent);
    fixture.detectChanges();
    httpMock.expectOne('/api/Admin/Accounts/u-1').flush(answer);
    await settle(fixture);
    return fixture;
  }

  it('should show the owner check rule and workspaces with members', async () => {
    const fixture = await render(card());
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('#owner-rule')?.textContent).toContain('Как проверить, что пишет владелец');
    expect(el.querySelector('.workspace-row')?.textContent).toContain('petya');
    expect(el.querySelector('.workspace-row')?.textContent).toContain('владелец');
    expect(el.querySelector('#activity')).toBeNull();
    expect(el.querySelector('#delete-account')).toBeNull();
  });

  it('should show the project counters when the card has them', async () => {
    const fixture = await render(card({
      // Счётчиков в ядре нет (`AccountActivityDto` пуст) — спека изображает раздел проекта
      activity: { issues: 3, notes: 0 } as unknown as AdminAccountCard['card']['activity']
    }));

    expect(fixture.nativeElement.querySelector('#activity')?.textContent).toContain('issues: 3');
    expect(fixture.nativeElement.querySelector('#activity')?.textContent).not.toContain('notes');
  });

  it('should search and open the found account', async () => {
    const fixture = await render(card());
    const el: HTMLElement = fixture.nativeElement;
    type(fixture, 'input[name="query"]', ' masha ');
    await settle(fixture);

    (el.querySelector('form button[type="submit"]') as HTMLButtonElement).click();
    const search = httpMock.expectOne(r => r.url === '/api/Admin/Accounts');
    expect(search.request.params.get('query')).toBe('masha');
    search.flush([{
      userId: 'u-2', userName: 'masha2', email: null, phoneNumber: null,
      hasTelegram: false, hasVk: true, registeredAtUtc: '2026-02-01T00:00:00Z'
    }]);
    await settle(fixture);

    (el.querySelector('.found-row') as HTMLElement).click();
    await new Promise(resolve => setTimeout(resolve));
    httpMock.expectOne('/api/Admin/Accounts/u-2').flush(card({ userId: 'u-2', userName: 'masha2' }));
    await settle(fixture);

    expect(el.querySelector('#account-card h3')?.textContent).toContain('masha2');
  });

  it('should offer deletion only to an account without workspaces', async () => {
    const fixture = await render(card({ workspaces: [] }));
    const el: HTMLElement = fixture.nativeElement;
    type(fixture, '#delete-reason', 'Забыл пароль');
    await settle(fixture);

    expect(el.querySelector('#issue-reset')).toBeNull();
    (el.querySelector('#delete-account') as HTMLButtonElement).click();
    await settle(fixture);
    (el.querySelector('#delete-confirm') as HTMLButtonElement).click();

    const request = httpMock.expectOne('/api/Admin/Accounts/u-1/Delete');
    expect(request.request.body).toEqual({ reason: 'Забыл пароль' });
    request.flush(null);
    await settle(fixture);
    expect(el.querySelector('#deleted')).not.toBeNull();
  });

  it('should not offer deletion of an admin', async () => {
    const fixture = await render(card({ workspaces: [] }, true));

    expect(fixture.nativeElement.querySelector('#delete-account')).toBeNull();
  });

  it('should not offer a reset link for an admin and say why', async () => {
    const fixture = await render(card({}, true));
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('#issue-reset')).toBeNull();
    expect(el.querySelector('#admin-account')?.textContent).toContain('роль в админке');
  });

  it('should not issue a link without a reason', async () => {
    const fixture = await render(card());

    expect((fixture.nativeElement.querySelector('#issue') as HTMLButtonElement).disabled).toBe(true);
  });

  it('should issue a reset link with a reason and show it to copy', async () => {
    const fixture = await render(card());
    const el: HTMLElement = fixture.nativeElement;
    type(fixture, '#reason', ' Назвала участников пространства ');
    await settle(fixture);

    (el.querySelector('#issue') as HTMLButtonElement).click();
    const request = httpMock.expectOne('/api/Admin/Accounts/u-1/IssueReset');
    expect(request.request.body).toEqual({ reason: 'Назвала участников пространства' });
    request.flush(ISSUED);
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Accounts/u-1').flush(card());
    await settle(fixture);

    expect((el.querySelector('#reset-url') as HTMLInputElement).value).toBe('https://example.com/auth/reset/abc');
    // Срок — в местном времени, как остальные даты админки, а не сырым ISO
    const text = el.querySelector('#issue-reset')?.textContent ?? '';
    expect(text).toContain(formatDate('2026-10-08T10:00:00Z', 'dd.MM.yyyy HH:mm', 'en-US'));
    expect(text).not.toContain('2026-10-08T10:00:00Z');
  });

  it('should say that a reissued link replaced the live one', async () => {
    const fixture = await render(card());
    const el: HTMLElement = fixture.nativeElement;
    type(fixture, '#reason', 'Потеряла ссылку');
    await settle(fixture);

    (el.querySelector('#issue') as HTMLButtonElement).click();
    httpMock.expectOne('/api/Admin/Accounts/u-1/IssueReset').flush({ ...ISSUED, replaced: true });
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Accounts/u-1').flush(card());
    await settle(fixture);

    expect(el.querySelector('#issue-reset')?.textContent).toContain('прежняя больше не работает');
  });

  it('should not keep the issued link and the form of the previous account', async () => {
    const fixture = await render(card());
    const el: HTMLElement = fixture.nativeElement;
    type(fixture, '#reason', 'Назвала участников пространства');
    await settle(fixture);
    (el.querySelector('#issue') as HTMLButtonElement).click();
    httpMock.expectOne('/api/Admin/Accounts/u-1/IssueReset').flush(ISSUED);
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Accounts/u-1').flush(card());
    await settle(fixture);
    expect(el.querySelector('#reset-url')).not.toBeNull();

    // Другой аккаунт из поиска — форма своя, без ссылки и причины прошлого
    const opening = (fixture.componentInstance as unknown as { open(id: string): Promise<void> }).open('u-2');
    await new Promise(resolve => setTimeout(resolve));
    httpMock.expectOne('/api/Admin/Accounts/u-2').flush(card({ userId: 'u-2', userName: 'petya' }));
    await opening;
    await settle(fixture);

    expect(el.querySelector('#account-card h3')?.textContent).toContain('petya');
    expect(el.querySelector('#reset-url')).toBeNull();
    expect((el.querySelector('#reason') as HTMLInputElement).value).toBe('');
  });

  it('should show the server refusal as is', async () => {
    const fixture = await render(card());
    const el: HTMLElement = fixture.nativeElement;
    type(fixture, '#reason', 'Попросили');
    await settle(fixture);

    (el.querySelector('#issue') as HTMLButtonElement).click();
    httpMock.expectOne('/api/Admin/Accounts/u-1/IssueReset').flush(
      { error: 'У аккаунта нет пространств — восстанавливать нечего, такой аккаунт удаляют', statusCode: 400 },
      { status: 400, statusText: 'Bad Request' });
    await settle(fixture);

    expect(el.querySelector('#issue-reset')?.textContent).toContain('У аккаунта нет пространств');
  });

  it('should release an email with a reason and reload the journal', async () => {
    const fixture = await render(card());
    const el: HTMLElement = fixture.nativeElement;
    type(fixture, '#release-address', '  masha@example.com ');
    type(fixture, '#release-reason', 'Прислала скриншот ящика');
    await settle(fixture);

    (el.querySelector('#release') as HTMLButtonElement).click();
    const request = httpMock.expectOne('/api/Admin/Accounts/u-1/ReleaseEmail');
    expect(request.request.body).toEqual({ email: 'masha@example.com', reason: 'Прислала скриншот ящика' });
    request.flush({ releasedFromUserName: 'squatter' });
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Accounts/u-1')
      .flush({ ...card(), actions: [{ kind: 3, adminName: 'boris', reason: 'Прислала скриншот', createdUtc: '2026-10-08T10:00:00Z' }] });
    await settle(fixture);

    expect(el.querySelector('#release-done')?.textContent).toContain('Почта снята с аккаунта squatter');
    expect(el.textContent).toContain('освобождение почты');
  });

  it('should release a phone with a reason and reload the journal', async () => {
    const fixture = await render(card());
    const el: HTMLElement = fixture.nativeElement;
    type(fixture, '#release-phone-number', ' +79990000005 ');
    type(fixture, '#release-phone-reason', 'Прислала договор с оператором');
    await settle(fixture);

    (el.querySelector('#release-phone-submit') as HTMLButtonElement).click();
    const request = httpMock.expectOne('/api/Admin/Accounts/u-1/ReleasePhone');
    expect(request.request.body).toEqual({ phone: '+79990000005', reason: 'Прислала договор с оператором' });
    request.flush({ releasedFromUserName: 'squatter' });
    await settle(fixture);
    httpMock.expectOne('/api/Admin/Accounts/u-1')
      .flush({ ...card(), actions: [{ kind: 5, adminName: 'boris', reason: 'Прислала договор', createdUtc: '2026-10-08T10:00:00Z' }] });
    await settle(fixture);

    expect(el.querySelector('#release-phone-done')?.textContent).toContain('Телефон снят с аккаунта squatter');
    expect(el.textContent).toContain('освобождение телефона');
  });

  it('should offer email release to an account without workspaces too', async () => {
    const fixture = await render(card({ workspaces: [] }));

    expect(fixture.nativeElement.querySelector('#release-email')).not.toBeNull();
  });

  it('should name the journal entry of an account the email was taken from', async () => {
    const fixture = await render({
      ...card(),
      actions: [{ kind: 4, adminName: null, reason: 'Чужой адрес · освобождён для masha', createdUtc: '2026-10-08T10:00:00Z' }]
    });

    expect(fixture.nativeElement.textContent).toContain('почту сняли для другого аккаунта');
    expect(fixture.nativeElement.textContent).toContain('удалённый админ');
  });
});
