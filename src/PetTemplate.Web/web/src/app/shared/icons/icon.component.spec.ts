import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import Mic from 'lucide/dist/esm/icons/mic.mjs';
import X from 'lucide/dist/esm/icons/x.mjs';
import { IconComponent } from './icon.component';

@Component({
  imports: [IconComponent],
  template: `<svg [appIcon]="icon()" [size]="size()" class="me-2"></svg>`
})
class HostComponent {
  readonly icon = signal(Mic);
  readonly size = signal<number | string>(18);
}

/** Иконка lucide из поштучного модуля: корень — атрибуты lucide, дети — узлы иконки */
describe('IconComponent', () => {
  async function render() {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection()]
    }).compileComponents();

    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    return { fixture, svg: () => (fixture.nativeElement as HTMLElement).querySelector('svg')! };
  }

  it('рисует узлы иконки элементами SVG с её атрибутами', async () => {
    const { svg } = await render();

    const children = Array.from(svg().children);
    expect(children.map(x => x.tagName)).toEqual(Mic.map(([tag]) => tag));
    expect(children.every(x => x.namespaceURI === 'http://www.w3.org/2000/svg')).toBe(true);
    expect(children[2].getAttribute('rx')).toBe('3');
  });

  it('корень — атрибуты lucide, размер и классы шаблона', async () => {
    const { svg } = await render();

    expect(svg().getAttribute('viewBox')).toBe('0 0 24 24');
    expect(svg().getAttribute('stroke')).toBe('currentColor');
    expect(svg().getAttribute('width')).toBe('18');
    expect(svg().getAttribute('height')).toBe('18');
    expect(svg().getAttribute('aria-hidden')).toBe('true');
    expect(svg().classList).toContain('lucide');
    expect(svg().classList).toContain('me-2');
  });

  it('смена иконки заменяет узлы, а не дописывает', async () => {
    const { fixture, svg } = await render();

    fixture.componentInstance.icon.set(X);
    fixture.componentInstance.size.set('1em');
    await fixture.whenStable();

    expect(svg().children.length).toBe(X.length);
    expect(svg().getAttribute('width')).toBe('1em');
  });
});
