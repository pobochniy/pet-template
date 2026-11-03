import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { BillingComponent } from './billing.component';
import { AdminSessionService } from '../core/admin-session.service';
import { AdminRole } from '../core/admin-role';
import { AdminBillingSummary, AdminBillingWorkspaceCard } from '../core/admin.model';

/** Ответ HTTP разворачивается цепочкой промисов компонента — даём ей дойти и перерисоваться */
async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  await fixture.whenStable();
  fixture.detectChanges();
}

const noLimit = { tokensPerDay: null, tokensPerMonth: null, costPerDay: null, costPerMonth: null };

function summary(): AdminBillingSummary {
  return {
    from: '2026-10-03',
    to: '2026-11-01',
    modelId: null,
    currency: 'RUB',
    runs: 12,
    tokens: 1_500_000,
    cost: 42.5,
    serviceCost: 1.25,
    service: [{ kind: 0, calls: 3, tokens: 9000, cost: 1.25 }],
    totalCost: 43.75,
    defaultLimit: { ...noLimit, tokensPerDay: 2_000_000 },
    models: [
      { modelId: 'm-1', displayName: 'Основная', provider: 'main', runs: 12, tokens: 1_500_000, cost: 42.5, serviceTokens: 9000, serviceCost: 1.25 }
    ],
    workspaces: [
      { workspaceId: 'w-1', workspaceName: 'Рога и Копыта', runs: 10, tokens: 1_000_000, cost: 40, ownLimit: true },
      { workspaceId: 'w-2', workspaceName: null, runs: 2, tokens: 500_000, cost: 2.5, ownLimit: false }
    ],
    providers: [
      {
        provider: 'main',
        facts: [],
        fact: null,
        estimate: 0,
        discrepancy: null,
        account: { hasPayments: true, paid: 1000, fact: 0, unbilled: 43.75, remaining: 956.25 }
      }
    ]
  };
}

function card(): AdminBillingWorkspaceCard {
  return {
    workspaceId: 'w-1',
    workspaceName: 'Рога и Копыта',
    currency: 'RUB',
    ownLimit: { ...noLimit, tokensPerDay: 100_000 },
    defaultLimit: { ...noLimit, tokensPerDay: 2_000_000 },
    limit: { ...noLimit, tokensPerDay: 100_000 },
    today: { tokens: 5000, cost: 0.5 },
    month: { tokens: 1_000_000, cost: 40 },
    from: '2026-10-03',
    to: '2026-11-01',
    users: [
      { userId: 'u-2', name: 'Vasya', runs: 8, tokens: 800_000, cost: 30 },
      { userId: 'u-3', name: null, runs: 2, tokens: 200_000, cost: 10 }
    ],
    limitUpdatedUtc: '2026-10-20T10:00:00Z',
    limitUpdatedBy: 'Oleg'
  };
}

/** «Расход» (спец. admin, 14): сводка любому админу, запись и лимиты — роли «Расход» */
describe('BillingComponent', () => {
  let httpMock: HttpTestingController;

  function signIn(roles: AdminRole[]): void {
    TestBed.inject(AdminSessionService).start({
      token: 'admin-token',
      expiresUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
      admin: { userId: 'u-1', userName: 'Oleg', roles }
    });
  }

  beforeEach(() => {
    localStorage.clear();
    // `end()` в `afterEach` уводит на `/login`: без маршрута переход падает уже после теста
    TestBed.configureTestingModule({
      imports: [BillingComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([{ path: 'login', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.inject(AdminSessionService).end();
    localStorage.clear();
  });

  async function render(roles: AdminRole[]) {
    signIn(roles);
    const fixture = TestBed.createComponent(BillingComponent);
    fixture.detectChanges();
    const request = httpMock.expectOne(r => r.url === '/api/Admin/Billing/Summary');
    request.flush(summary());
    // Второй круг — снять «занято» с кнопок: finally компонента отрабатывает после ответа
    await settle(fixture);
    await settle(fixture);
    return { fixture, element: fixture.nativeElement as HTMLElement, request };
  }

  async function openCard(fixture: Awaited<ReturnType<typeof render>>['fixture'], element: HTMLElement) {
    (element.querySelector('.workspace-row button') as HTMLButtonElement).click();
    httpMock.expectOne(r => r.url === '/api/Admin/Billing/Workspaces/w-1').flush(card());
    await settle(fixture);
  }

  it('should ask for the last 30 days and show totals', async () => {
    const { element, request } = await render([AdminRole.Accounts]);

    expect(request.request.params.get('from')).toMatch(/^\d{4}-\d\d-\d\d$/);
    expect(request.request.params.has('modelId')).toBe(false);
    expect(element.querySelector('#total-cost')!.textContent!.trim()).toBe('43,75');
    expect(element.querySelector('#default-limit')!.textContent).toContain('2 млн ток. в сутки');
  });

  it('should show a deleted workspace without a link', async () => {
    const { element } = await render([AdminRole.Accounts]);

    const rows = element.querySelectorAll('.workspace-row');
    expect(rows.length).toBe(2);
    expect(rows[1].querySelector('button')).toBeNull();
    expect(rows[1].textContent).toContain('удалено');
  });

  it('should hide writing without the Billing role', async () => {
    const { fixture, element } = await render([AdminRole.Accounts]);
    await openCard(fixture, element);

    expect(element.textContent).not.toContain('записать платёж');
    expect(element.querySelector('.limit-form')).toBeNull();
    expect(element.querySelectorAll('.user-row').length).toBe(2);
    expect(element.textContent).toContain('аккаунт удалён');
  });

  it('should save the own limit with empty fields as no ceiling', async () => {
    const { fixture, element } = await render([AdminRole.Billing]);
    await openCard(fixture, element);

    const monthly = element.querySelector('#tokensPerMonth') as HTMLInputElement;
    monthly.value = '3000000';
    monthly.dispatchEvent(new Event('input'));
    const daily = element.querySelector('#tokensPerDay') as HTMLInputElement;
    daily.value = '';
    daily.dispatchEvent(new Event('input'));
    (element.querySelector('#save-limit') as HTMLButtonElement).click();
    await settle(fixture);

    const put: TestRequest = httpMock.expectOne('/api/Admin/Billing/Workspaces/w-1/Limit');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ tokensPerDay: null, tokensPerMonth: 3_000_000, costPerDay: null, costPerMonth: null });
    put.flush(null);
    await settle(fixture);
    httpMock.expectOne(r => r.url === '/api/Admin/Billing/Summary').flush(summary());
    await settle(fixture);
    httpMock.expectOne(r => r.url === '/api/Admin/Billing/Workspaces/w-1').flush(card());
    await settle(fixture);
  });

  it('should reset the own limit to the default', async () => {
    const { fixture, element } = await render([AdminRole.Billing]);
    await openCard(fixture, element);

    (element.querySelector('#reset-limit') as HTMLButtonElement).click();
    await settle(fixture);

    const reset = httpMock.expectOne('/api/Admin/Billing/Workspaces/w-1/Limit');
    expect(reset.request.method).toBe('DELETE');
    reset.flush(null);
    await settle(fixture);
    httpMock.expectOne(r => r.url === '/api/Admin/Billing/Summary').flush(summary());
    await settle(fixture);
    httpMock.expectOne(r => r.url === '/api/Admin/Billing/Workspaces/w-1').flush({ ...card(), ownLimit: null });
    await settle(fixture);
  });

  it('should show the server refusal as is', async () => {
    const { fixture, element } = await render([AdminRole.Billing]);

    const buttons = Array.from(element.querySelectorAll('.provider button')) as HTMLButtonElement[];
    buttons.find(x => x.textContent!.includes('записать платёж'))!.click();
    await settle(fixture);
    (element.querySelector('.ledger-form button[type=submit]') as HTMLButtonElement).click();
    await settle(fixture);

    httpMock.expectOne('/api/Admin/Billing/Ledger').flush({ error: 'Сумма не может быть нулевой' }, { status: 400, statusText: 'Bad Request' });
    await settle(fixture);
    await settle(fixture);

    expect(element.querySelector('#billing-error')!.textContent!.trim()).toBe('Сумма не может быть нулевой');
  });
});
