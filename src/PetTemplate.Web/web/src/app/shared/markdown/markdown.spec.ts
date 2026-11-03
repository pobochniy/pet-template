import { TestBed } from '@angular/core/testing';
import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { MdBlock, ownRoute, parseInline, parseMarkdown } from './markdown';
import { MarkdownComponent } from './markdown.component';

describe('parseMarkdown', () => {
  it('should split paragraphs by blank lines and keep single newlines as breaks', () => {
    const blocks = parseMarkdown('one\ntwo\n\nthree');

    expect(blocks.length).toBe(2);
    expect(blocks[0]).toEqual({
      kind: 'paragraph',
      inlines: [{ kind: 'text', text: 'one' }, { kind: 'break' }, { kind: 'text', text: 'two' }]
    });
  });

  it('should start a list right after a paragraph line, as help articles write it', () => {
    const blocks = parseMarkdown('In the menu:\n- Home\n- Profile\n\nAfter.');

    expect(blocks.map(b => b.kind)).toEqual(['paragraph', 'list', 'paragraph']);
    const list = blocks[1] as Extract<MdBlock, { kind: 'list' }>;
    expect(list.ordered).toBe(false);
    expect(list.items.length).toBe(2);
  });

  it('should parse ordered and nested lists', () => {
    const [list] = parseMarkdown('3. first\n   - inner\n4. second') as Extract<MdBlock, { kind: 'list' }>[];

    expect(list.ordered).toBe(true);
    expect(list.start).toBe(3);
    expect(list.items.length).toBe(2);
    expect(list.items[0].children[0].items[0].inlines).toEqual([{ kind: 'text', text: 'inner' }]);
  });

  it('should keep code blocks verbatim, markup inside them included', () => {
    const blocks = parseMarkdown('```ts\nconst a = **b**;\n<script>x</script>\n```\nafter');

    expect(blocks[0]).toEqual({ kind: 'code', text: 'const a = **b**;\n<script>x</script>' });
    expect(blocks[1].kind).toBe('paragraph');
  });

  it('should parse headings', () => {
    expect(parseMarkdown('## How to invite')[0]).toEqual({
      kind: 'heading', level: 2, inlines: [{ kind: 'text', text: 'How to invite' }]
    });
  });
});

describe('parseInline', () => {
  it('should parse bold, italic and inline code', () => {
    expect(parseInline('a **b** *c* `d*e*`')).toEqual([
      { kind: 'text', text: 'a ' },
      { kind: 'strong', children: [{ kind: 'text', text: 'b' }] },
      { kind: 'text', text: ' ' },
      { kind: 'em', children: [{ kind: 'text', text: 'c' }] },
      { kind: 'text', text: ' ' },
      { kind: 'code', text: 'd*e*' }
    ]);
  });

  it('should not treat snake_case or spaced asterisks as emphasis', () => {
    expect(parseInline('snake_case_name and 2 * 3 * 4')).toEqual([{ kind: 'text', text: 'snake_case_name and 2 * 3 * 4' }]);
  });

  it('should make links only to own routes', () => {
    expect(parseInline('[members](/workspace/members)')).toEqual([
      { kind: 'link', href: '/workspace/members', children: [{ kind: 'text', text: 'members' }] }
    ]);
  });

  it('should leave foreign and script addresses as text', () => {
    for (const href of ['https://evil.example', '//evil.example', '/\\evil.example', 'javascript:alert(1)', 'profile']) {
      const nodes = parseInline(`[x](${href})`);
      expect(nodes.some(n => n.kind === 'link'), href).toBe(false);
      expect(nodes.map(n => (n.kind === 'text' ? n.text : '')).join(''), href).toBe(`x (${href})`);
    }
  });

  it('should honour backslash escapes', () => {
    expect(parseInline('\\*not italic\\*')).toEqual([{ kind: 'text', text: '*not italic*' }]);
  });
});

describe('ownRoute', () => {
  it('should accept app paths with query and fragment', () => {
    expect(ownRoute('/profile/edit?tab=1#top')).toBe('/profile/edit?tab=1#top');
  });

  it('should refuse other sites and control characters', () => {
    expect(ownRoute('//x.example')).toBeNull();
    expect(ownRoute('/a\nb')).toBeNull();
    expect(ownRoute('https://x.example/')).toBeNull();
  });
});

@Component({
  imports: [MarkdownComponent],
  template: `<app-markdown [text]="text()" />`
})
class HostComponent {
  readonly text = signal('');
}

describe('MarkdownComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection(), provideRouter([])]
    });
  });

  async function render(text: string): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.text.set(text);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('should render blocks as elements, raw HTML as text', async () => {
    const el = await render('## Title\n\nText with <b>tag</b> and **bold**\n\n1. one\n2. two\n\n```\ncode\n```');

    expect(el.querySelector('h2')?.textContent).toBe('Title');
    expect(el.querySelector('b')).toBeNull();
    expect(el.querySelector('p')?.textContent).toBe('Text with <b>tag</b> and bold');
    expect(el.querySelector('p strong')?.textContent).toBe('bold');
    expect(el.querySelectorAll('ol li').length).toBe(2);
    expect(el.querySelector('pre code')?.textContent).toBe('code');
  });

  it('should render own links as router links', async () => {
    const el = await render('See [settings](/workspace/settings) or [site](https://example.com).');

    const links = el.querySelectorAll('a');
    expect(links.length).toBe(1);
    expect(links[0].getAttribute('href')).toBe('/workspace/settings');
    expect(el.textContent).toContain('site (https://example.com)');
  });
});
