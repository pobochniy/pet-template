import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideRouter } from '@angular/router';
import { provideExperimentalZonelessChangeDetection } from '@angular/core';
import { LoginComponent } from './login.component';
import { AuthApiService } from '../../shared/api/auth-api.service';
import { AuthApiServiceMock } from '../../shared/api/auth-api.service.mock';
import { ChatService } from '../../shared/chat/chat.service';
import { ChatServiceMock } from '../../shared/chat/chat.service.mock';
import { UserService } from '../../shared/core/user.service';
import { AlertsService } from '../../shared/alerts/alerts.service';

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;
  let authService: AuthApiServiceMock;
  let chatService: ChatServiceMock;
  let userService: UserService;
  let alertsService: AlertsService;
  let router: Router;

  beforeEach(async () => {
    localStorage.clear();
    
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideExperimentalZonelessChangeDetection(),
        provideRouter([]),
        { provide: AuthApiService, useClass: AuthApiServiceMock },
        { provide: ChatService, useClass: ChatServiceMock },
        UserService,
        AlertsService
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    authService = TestBed.inject(AuthApiService) as unknown as AuthApiServiceMock;
    chatService = TestBed.inject(ChatService) as unknown as ChatServiceMock;
    userService = TestBed.inject(UserService);
    alertsService = TestBed.inject(AlertsService);
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  afterEach(() => {
    alertsService.alerts.set([]);
    localStorage.clear();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should call chat service methods on successful login', async () => {
    component.loginForm.patchValue({ login: 'testUser', password: 'password123' });

    await component.onSubmit();
    fixture.detectChanges();

    expect(chatService.initConnectionCalled).toBeTrue();
    expect(chatService.connectionWebSocketCalled).toBeTrue();
  });

  it('should set user in userService on successful login', async () => {
    component.loginForm.patchValue({ login: 'testUser', password: 'password123' });

    await component.onSubmit();
    fixture.detectChanges();

    expect(userService.User).toBeDefined();
    expect(userService.User?.id).toBe('1');
    expect(userService.User?.userName).toBe('testUser');
    expect(userService.User?.email).toBe('test@example.com');
  });

  it('should show error alert when login fails', async () => {
    authService.setupFailure();
    component.loginForm.patchValue({ login: 'wrongUser', password: 'wrongPassword' });

    await component.onSubmit();
    fixture.detectChanges();

    const alerts = alertsService.alerts();
    expect(alerts.length).toBe(1);
    expect(alerts[0].alertClass).toBe('danger');
    expect(alerts[0].content).toBe('Неверный логин или пароль');
  });

  it('should show validation errors from ModelState when 422 response', async () => {
    authService.setupValidationError();
    component.loginForm.patchValue({ login: 'abc', password: '123' });

    await component.onSubmit();
    fixture.detectChanges();

    const alerts = alertsService.alerts();
    expect(alerts.length).toBe(1);
    expect(alerts[0].alertClass).toBe('danger');

    const alertContent = alerts[0].content;
    expect(alertContent).toContain('Логин должен содержать минимум 3 символа');
    expect(alertContent).toContain('Пароль слишком простой');
  });

  it('should navigate to home on successful login', async () => {
    component.loginForm.patchValue({ login: 'testUser', password: 'password123' });

    spyOn(router, 'navigateByUrl');

    await component.onSubmit();
    fixture.detectChanges();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('should not submit when form is invalid', async () => {
    component.loginForm.patchValue({ login: '', password: '' });

    await component.onSubmit();
    fixture.detectChanges();

    expect(userService.User).toBeUndefined();
    expect(chatService.initConnectionCalled).toBeFalse();
  });
});
