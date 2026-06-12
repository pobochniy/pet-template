import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { HomeComponent } from './home.component';
import { UserService } from '../shared/services/user.service';
import { AuthApiService } from '../shared/api/auth-api.service';
import { GameApiService } from '../shared/api/game-api.service';
import { ProfileModel } from '../shared/models/profile.model';

describe('HomeComponent', () => {
  let component: HomeComponent;
  let fixture: ComponentFixture<HomeComponent>;
  let mockUserService: jasmine.SpyObj<UserService>;
  let mockAuthService: jasmine.SpyObj<AuthApiService>;
  let mockGameService: jasmine.SpyObj<GameApiService>;
  let router: Router;

  beforeEach(async () => {
    mockUserService = jasmine.createSpyObj('UserService', [], {
      profile: signal<ProfileModel | null>(null)
    });
    mockAuthService = jasmine.createSpyObj('AuthApiService', ['logout']);
    Object.defineProperty(mockAuthService, 'isAuth', {
      get: jasmine.createSpy('isAuth').and.returnValue(true),
      configurable: true
    });
    mockGameService = jasmine.createSpyObj('GameApiService', ['newGame']);

    await TestBed.configureTestingModule({
      imports: [HomeComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: UserService, useValue: mockUserService },
        { provide: AuthApiService, useValue: mockAuthService },
        { provide: GameApiService, useValue: mockGameService },
      ]
    }).compileComponents();

    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.returnValue(Promise.resolve(true));
    spyOn(router, 'navigateByUrl').and.returnValue(Promise.resolve(true));

    fixture = TestBed.createComponent(HomeComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('ngOnInit', () => {
    it('should not redirect if user is authenticated', async () => {
      (Object.getOwnPropertyDescriptor(mockAuthService, 'isAuth')?.get as jasmine.Spy).and.returnValue(true);

      await component.ngOnInit();

      expect(router.navigateByUrl).not.toHaveBeenCalled();
    });

    it('should redirect to miniapp if user is not authenticated', async () => {
      (Object.getOwnPropertyDescriptor(mockAuthService, 'isAuth')?.get as jasmine.Spy).and.returnValue(false);

      await component.ngOnInit();

      expect(router.navigateByUrl).toHaveBeenCalledWith('/miniapp');
    });
  });

  describe('logout', () => {
    it('should logout and redirect to miniapp', async () => {
      mockAuthService.logout.and.returnValue(Promise.resolve());

      await component.logout();

      expect(mockAuthService.logout).toHaveBeenCalled();
      expect(router.navigateByUrl).toHaveBeenCalledWith('/miniapp');
    });
  });

  describe('startQuickGame', () => {
    it('should create a new game and navigate to game page', async () => {
      mockGameService.newGame.and.returnValue(Promise.resolve('test-game-123'));

      await component.startQuickGame();

      expect(mockGameService.newGame).toHaveBeenCalledWith({
        botsCount: jasmine.any(Number),
        attackerType: jasmine.any(Number),
        deckSize: jasmine.any(Number),
        gameSpeed: jasmine.any(Number),
        gameType: jasmine.any(Number),
        playerCount: jasmine.any(Number),
        bet: 0
      });
      expect(router.navigate).toHaveBeenCalledWith(['/game', 'test-game-123']);
    });

    it('should handle error when starting game', async () => {
      spyOn(console, 'error');
      mockGameService.newGame.and.returnValue(Promise.reject(new Error('Failed to start game')));

      await component.startQuickGame();

      expect(console.error).toHaveBeenCalledWith('Failed to start game:', jasmine.any(Error));
      expect(router.navigate).not.toHaveBeenCalled();
    });
  });
});
