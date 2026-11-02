import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';
import { serviceCollection } from './app.providers';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        ...serviceCollection
      ]
    }).compileComponents();
  });

  it('should render the shell: menu, header and the product name', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('shared-nav-menu')).not.toBeNull();
    expect(element.querySelector('top-nav')).not.toBeNull();
    expect(element.querySelector('.sidebar-header')?.textContent).toContain('PetTemplate');
  });
});
