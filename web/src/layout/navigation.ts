import { Boxes, LayoutDashboard, Palette, type LucideIcon } from 'lucide-react';

export interface NavItem {
  to: '/' | '/sample' | '/ui-kit';
  labelKey: 'nav.home' | 'nav.sample' | 'nav.uiKit';
  icon: LucideIcon;
}

/**
 * Sidebar entries. From Phase 2 the list is filtered by system access and the Settings module switches,
 * so users only see systems they can open.
 */
export const navigation: readonly NavItem[] = [
  { to: '/', labelKey: 'nav.home', icon: LayoutDashboard },
  { to: '/sample', labelKey: 'nav.sample', icon: Boxes },
  { to: '/ui-kit', labelKey: 'nav.uiKit', icon: Palette },
];
