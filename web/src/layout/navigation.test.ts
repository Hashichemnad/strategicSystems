import { describe, expect, it } from 'vitest';
import { visibleNavigation } from './navigation';

const routes = (access: Parameters<typeof visibleNavigation>[0]) =>
  visibleNavigation(access).flatMap((section) => section.items.map((item) => item.to));

describe('sidebar filtered by real access', () => {
  it('shows only what the user may open', () => {
    const plain = routes({
      systems: ['org', 'identity', 'access', 'settings'],
      permissions: ['org.unit.read', 'org.employee.read'],
      roles: [],
    });
    expect(plain).toContain('/org/units');
    expect(plain).not.toContain('/sample');
    expect(plain).not.toContain('/settings');
    expect(plain).not.toContain('/admin/users');
  });

  it('shows a system once it is enabled and granted, and the settings centre for any settings permission', () => {
    const editor = routes({
      systems: ['org', 'sample'],
      permissions: ['org.unit.read', 'sample.item.read', 'settings.lookup.manage'],
      roles: ['sample.editor'],
    });
    expect(editor).toContain('/sample');
    expect(editor).toContain('/settings');
    expect(editor).not.toContain('/admin/security');
  });

  it('shows nothing guarded while access is still loading', () => {
    expect(routes(undefined)).toEqual(['/', '/ui-kit']);
  });
});
