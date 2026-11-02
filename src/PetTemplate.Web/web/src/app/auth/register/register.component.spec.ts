import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { Router, provideRouter } from '@angular/router';
import { RegisterComponent } from './register.component';
import { AuthApiService } from '../../shared/api/auth-api.service';
import { AlertsService } from '../../shared/alerts/alerts.service';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';
import { serverRefusal, validationRefusal } from '../../shared/api/server-refusal.mock';
import { ExternalLoginApiService } from '../../shared/api/external-login-api.service';
import { ExternalLoginApiServiceMock } from '../../shared/api/external-login-api.service.mock';

describe('RegisterComponent', () => {
  let fixture: ComponentFixture<RegisterComponent>;
  let el: HTMLElement;
  let auth: jasmine.SpyObj<AuthApiService>;

  beforeEach(async () => {
    auth = jasmine.createSpyObj<AuthApiService>('AuthApiService', ['register']);

    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ExternalLoginApiService, useClass: ExternalLoginApiServiceMock },
        provideRouter([]),
        { provide: AuthApiService, useValue: auth }
      ]
    }).compileComponents();

    await loadAllScopes(TestBed.inject(I18nService));
    spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    fixture = TestBed.createComponent(RegisterComponent);
    el = fixture.nativeElement;
    await fixture.whenStable();
  });

  function type(id: string, value: string) {
    const input = el.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function fillRequired() {
    type('userName', 'Boris');
    type('password', 'Parol-Borisa-42');
    type('passwordConfirm', 'Parol-Borisa-42');
    type('workspaceName', 'Рога и Копыта');
  }

  async function submit() {
    el.querySelector<HTMLButtonElement>('#register-submit')!.click();
    await new Promise(resolve => setTimeout(resolve));
    await fixture.whenStable();
  }

  it('should keep the phone and email under "More fields"', async () => {
    const optional = () => el.querySelector('#register-optional-fields')!;
    expect(optional().classList).toContain('d-none');
    expect(optional().querySelector('#phone')).not.toBeNull();
    expect(el.querySelector('#register-optional-toggle')!.textContent).toContain('Дополнительные поля');

    el.querySelector<HTMLButtonElement>('#register-optional-toggle')!.click();
    await fixture.whenStable();

    expect(optional().classList).not.toContain('d-none');
  });

  it('should reject a reserved user name before sending it', async () => {
    fillRequired();
    type('userName', 'tg123');

    await submit();

    expect(auth.register).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Логины вида tg123 и vk123 заняты под автоматические');
  });

  it('should show a taken user name in the interface language', async () => {
    auth.register.and.rejectWith(serverRefusal(400, 'Такое имя пользователя уже занято', 'auth.error.userNameTaken'));
    fillRequired();

    await submit();

    expect(TestBed.inject(AlertsService).alerts().map(a => a.content)).toEqual(['Такое имя пользователя уже занято']);
  });

  it('should open the optional fields to show a server refusal under the phone', async () => {
    auth.register.and.rejectWith(validationRefusal({ phone: [{ code: 'core.validation.phoneFormat' }] }));
    fillRequired();

    await submit();

    expect(el.querySelector('#register-optional-fields')!.classList).not.toContain('d-none');
    expect(el.textContent).toContain('Телефон — плюс и 11 цифр');
  });
});
