import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { ComponentFixture } from '@angular/core/testing';
import { WalletComponent } from './wallet.component';
import { WalletApiService } from '../../shared/api/wallet-api.service';
import { UserService } from '../../shared/services/user.service';
import { ProfileModel } from '../../shared/models/profile.model';

describe('WalletComponent', () => {
  let fixture: ComponentFixture<WalletComponent>;

  function buildProfile(walletAddress: string | null): ProfileModel {
    return {
      id: '00000000-0000-0000-0000-000000000001',
      userName: 'Олег',
      avatarId: 0,
      comment: null,
      isAdult: true,
      hasAcceptedTerms: true,
      walletAddress
    };
  }

  function makeUserServiceStub(walletAddress: string | null): Partial<UserService> {
    const profileSignal = signal<ProfileModel | null>(buildProfile(walletAddress));
    const isConnected = walletAddress !== null;
    return {
      profile: profileSignal.asReadonly(),
      isWalletConnected: signal(isConnected).asReadonly() as any,
      setProfile: jasmine.createSpy('setProfile')
    };
  }

  function makeWalletApiSpy(getBalanceResult: 'resolve' | 'reject' = 'resolve'): jasmine.SpyObj<WalletApiService> {
    const spy = jasmine.createSpyObj('WalletApiService', ['connectWallet', 'disconnectWallet', 'getBalance']);
    if (getBalanceResult === 'resolve') {
      spy.getBalance.and.resolveTo({ balanceTon: 1.23 });
    } else {
      spy.getBalance.and.rejectWith(new Error('Network error'));
    }
    return spy;
  }

  it('should display connect button when no wallet linked', async () => {
    TestBed.configureTestingModule({
      imports: [WalletComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: WalletApiService, useValue: makeWalletApiSpy() },
        { provide: UserService, useValue: makeUserServiceStub(null) }
      ]
    });
    fixture = TestBed.createComponent(WalletComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const button = fixture.debugElement.query(By.css('button.btn-primary'));
    expect(button).toBeTruthy();
    expect(button.nativeElement.textContent).toContain('Подключить кошелёк');
  });

  it('should display wallet address truncated when connected', async () => {
    TestBed.configureTestingModule({
      imports: [WalletComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: WalletApiService, useValue: makeWalletApiSpy() },
        { provide: UserService, useValue: makeUserServiceStub('EQA123456789abcdefghij') }
      ]
    });
    fixture = TestBed.createComponent(WalletComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const addressEl = fixture.debugElement.query(By.css('.wallet-address'));
    expect(addressEl).toBeTruthy();
    expect(addressEl.nativeElement.textContent).toContain('EQA123');
    expect(addressEl.nativeElement.textContent).toContain('ghij');
  });

  it('should display balance when loaded', async () => {
    TestBed.configureTestingModule({
      imports: [WalletComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: WalletApiService, useValue: makeWalletApiSpy('resolve') },
        { provide: UserService, useValue: makeUserServiceStub('EQA1234567890abcdefghijklmnopqrstuvwxyz') }
      ]
    });
    fixture = TestBed.createComponent(WalletComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const balanceEl = fixture.debugElement.query(By.css('.wallet-balance-value'));
    expect(balanceEl).toBeTruthy();
    expect(balanceEl.nativeElement.textContent).toContain('1.23 TON');
  });

  it('should show error when balance fetch fails', async () => {
    TestBed.configureTestingModule({
      imports: [WalletComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: WalletApiService, useValue: makeWalletApiSpy('reject') },
        { provide: UserService, useValue: makeUserServiceStub('EQA1234567890abcdefghijklmnopqrstuvwxyz') }
      ]
    });
    fixture = TestBed.createComponent(WalletComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const errorEl = fixture.debugElement.query(By.css('.text-danger'));
    expect(errorEl).toBeTruthy();
    const retryBtn = fixture.debugElement.query(By.css('.btn-outline-secondary'));
    expect(retryBtn).toBeTruthy();
  });
});
