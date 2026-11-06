import { TestBed } from '@angular/core/testing';
import { Avatar, initialsOf } from './avatar';

describe('Avatar', () => {
  it('takes the first letters of the first two words', () => {
    expect(initialsOf('Ana Maria Pop')).toBe('AM');
    expect(initialsOf('  ștefan  ')).toBe('Ș');
    expect(initialsOf('ana.pop@example.com')).toBe('AP');
  });

  it('is decorative next to the name it shows', async () => {
    const fixture = TestBed.createComponent(Avatar);
    fixture.componentRef.setInput('name', 'Ana Pop');
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent.trim()).toBe('AP');
    expect(host.getAttribute('aria-hidden')).toBe('true');
    expect(host.getAttribute('role')).toBeNull();
  });

  it('says whose it is when it stands alone', async () => {
    const fixture = TestBed.createComponent(Avatar);
    fixture.componentRef.setInput('name', 'Ana Pop');
    fixture.componentRef.setInput('labelled', true);
    fixture.componentRef.setInput('size', 32);
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.getAttribute('role')).toBe('img');
    expect(host.getAttribute('aria-label')).toBe('Ana Pop');
    expect(host.classList).toContain('large');
  });
});
