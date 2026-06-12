import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, ActivatedRoute } from '@angular/router';
import { provideZonelessChangeDetection } from '@angular/core';
import { ViewComponent } from './view.component';
import { ProfileApiService } from '../../shared/api/profile-api.service';
import { ProfileModel } from '../../shared/models/profile.model';

describe('ViewComponent', () => {
  let component: ViewComponent;
  let fixture: ComponentFixture<ViewComponent>;
  let mockProfileService: jasmine.SpyObj<ProfileApiService>;
  let mockRouter: jasmine.SpyObj<Router>;
  let mockActivatedRoute: any;

  beforeEach(async () => {
    mockProfileService = jasmine.createSpyObj('ProfileApiService', ['view']);
    mockRouter = jasmine.createSpyObj('Router', ['navigateByUrl']);
    mockActivatedRoute = {
      snapshot: {
        paramMap: {
          get: jasmine.createSpy('get').and.returnValue('123')
        }
      }
    };

    await TestBed.configureTestingModule({
      imports: [ViewComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ProfileApiService, useValue: mockProfileService },
        { provide: Router, useValue: mockRouter },
        { provide: ActivatedRoute, useValue: mockActivatedRoute }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ViewComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('ngOnInit', () => {
    it('should load profile by userId', async () => {
      const mockProfile: ProfileModel = {
        id: '123',
        userName: 'TestUser',
        avatarId: 4,
        comment: 'Test comment',
        isAdult: true,
        hasAcceptedTerms: true,
        walletAddress: null
      };

      mockProfileService.view.and.returnValue(Promise.resolve(mockProfile));

      await component.ngOnInit();

      expect(mockProfileService.view).toHaveBeenCalledWith('123');
      expect(component.profile).toEqual(mockProfile);
      expect(mockRouter.navigateByUrl).not.toHaveBeenCalled();
    });

    it('should redirect to home if userId is null', async () => {
      // Пересоздаем компонент с null userId
      mockActivatedRoute.snapshot.paramMap.get = jasmine.createSpy('get').and.returnValue(null);
      
      const newFixture = TestBed.createComponent(ViewComponent);
      const newComponent = newFixture.componentInstance;
      
      mockRouter.navigateByUrl.and.returnValue(Promise.resolve(true));

      await newComponent.ngOnInit();

      expect(mockProfileService.view).not.toHaveBeenCalled();
      expect(mockRouter.navigateByUrl).toHaveBeenCalledWith('/');
    });

    it('should redirect to home if profile is not found', async () => {
      mockProfileService.view.and.returnValue(Promise.resolve(null as any));
      mockRouter.navigateByUrl.and.returnValue(Promise.resolve(true));

      await component.ngOnInit();

      expect(mockProfileService.view).toHaveBeenCalledWith('123');
      expect(mockRouter.navigateByUrl).toHaveBeenCalledWith('/');
    });
  });

  describe('userId', () => {
    it('should get userId from route params', () => {
      expect(component.userId).toBe('123');
    });
  });
});
