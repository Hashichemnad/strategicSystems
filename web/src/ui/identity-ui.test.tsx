import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { Avatar, Checkbox, FileDrop, OrgChart, OtpInput, Switch, TreeView } from '@/ui';
import { buildTree } from './tree';

interface Unit {
  id: string;
  parentId: string | null;
  name: string;
}

const units: Unit[] = [
  { id: 'org', parentId: null, name: 'Organisation' },
  { id: 'sector', parentId: 'org', name: 'Sector' },
  { id: 'dept', parentId: 'sector', name: 'Department' },
  { id: 'section', parentId: 'dept', name: 'Section' },
  { id: 'finance', parentId: 'org', name: 'Finance' },
];

function ControlledOtp({ onChange }: { onChange?: (value: string) => void }) {
  const [value, setValue] = useState('');
  return (
    <OtpInput
      label="Code"
      value={value}
      onChange={(next) => {
        setValue(next);
        onChange?.(next);
      }}
    />
  );
}

describe('buildTree', () => {
  it('nests children under parents, keeps order and treats orphans as roots', () => {
    const roots = buildTree([...units, { id: 'orphan', parentId: 'missing', name: 'Orphan' }]);
    expect(roots.map((n) => n.item.id)).toEqual(['org', 'orphan']);
    expect(roots[0]?.children.map((n) => n.item.id)).toEqual(['sector', 'finance']);
    expect(roots[0]?.children[0]?.children[0]?.children[0]).toMatchObject({
      item: { id: 'section' },
      depth: 3,
    });
  });
});

describe('OtpInput', () => {
  it('moves focus as digits are typed, ignores letters and steps back on Backspace', async () => {
    const onChange = vi.fn();
    await renderWithProviders(<ControlledOtp onChange={onChange} />);
    const boxes = screen.getAllByRole('textbox');
    expect(boxes).toHaveLength(6);
    const [first] = boxes;
    if (!first) throw new Error('no boxes');
    expect(screen.getByRole('group', { name: 'Code' })).toHaveAttribute('dir', 'ltr');

    await userEvent.click(first);
    await userEvent.keyboard('12a3');
    expect(onChange).toHaveBeenLastCalledWith('123');
    expect(boxes[3]).toHaveFocus();

    await userEvent.keyboard('{Backspace}');
    expect(boxes[2]).toHaveFocus();
    expect(onChange).toHaveBeenLastCalledWith('12');
  });

  it('fills every box from a pasted code', async () => {
    const onChange = vi.fn();
    await renderWithProviders(<ControlledOtp onChange={onChange} />);
    await userEvent.click(screen.getAllByLabelText('Code 1')[0] ?? document.body);
    await userEvent.paste('654 321');
    expect(onChange).toHaveBeenLastCalledWith('654321');
    expect(screen.getAllByRole('textbox').map((box) => (box as HTMLInputElement).value)).toEqual([
      '6',
      '5',
      '4',
      '3',
      '2',
      '1',
    ]);
  });
});

describe('Switch and Checkbox', () => {
  it('Switch exposes role switch with its state and label', async () => {
    const onChange = vi.fn();
    await renderWithProviders(<Switch checked={false} onCheckedChange={onChange} label="Enabled" />);
    const control = screen.getByRole('switch', { name: 'Enabled' });
    expect(control).toHaveAttribute('aria-checked', 'false');
    await userEvent.click(control);
    expect(onChange).toHaveBeenCalledWith(true);
  });

  it('Checkbox links its description', async () => {
    await renderWithProviders(<Checkbox label="Remember" description="On this device" />);
    expect(screen.getByRole('checkbox', { name: 'Remember' })).toHaveAccessibleDescription('On this device');
  });
});

describe('Avatar and FileDrop', () => {
  it('Avatar shows initials and keeps the name for screen readers', async () => {
    await renderWithProviders(<Avatar name="mariam al ali" />);
    expect(screen.getByText('MA')).toBeInTheDocument();
    expect(screen.getByText('mariam al ali')).toHaveClass('sr-only');
  });

  it('FileDrop hands the chosen file over', async () => {
    const onFile = vi.fn();
    await renderWithProviders(<FileDrop label="Choose a file" accept=".xlsx" file={null} onFile={onFile} />);
    const file = new File(['x'], 'units.xlsx');
    await userEvent.upload(screen.getByLabelText('Choose a file'), file);
    expect(onFile).toHaveBeenCalledWith(file);
  });
});

describe('TreeView', () => {
  const renderTree = (language: 'ar' | 'en', onSelect = vi.fn()) =>
    renderWithProviders(
      <TreeView items={units} label="Units" onSelect={onSelect} renderLabel={(unit) => unit.name} />,
      { language },
    );

  it('starts with the roots expanded and selects with Enter', async () => {
    const onSelect = vi.fn();
    await renderTree('en', onSelect);
    expect(screen.getAllByRole('treeitem').map((item) => item.textContent)).toEqual([
      'Organisation',
      'Sector',
      'Finance',
    ]);
    screen.getByRole('treeitem', { name: 'Organisation' }).focus();
    await userEvent.keyboard('{ArrowDown}{Enter}');
    expect(onSelect).toHaveBeenCalledWith(units[1]);
  });

  it('opens the branch of a unit selected from outside', async () => {
    await renderWithProviders(
      <TreeView
        items={units}
        label="Units"
        selectedId="section"
        onSelect={vi.fn()}
        renderLabel={(u) => u.name}
      />,
    );
    expect(screen.getByRole('treeitem', { name: 'Section' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('treeitem', { name: 'Department' })).toHaveAttribute('aria-expanded', 'true');
  });

  it.each([
    ['en', 'ArrowRight', 'ArrowLeft'],
    ['ar', 'ArrowLeft', 'ArrowRight'],
  ] as const)('in %s, %s expands and %s collapses', async (language, inward, outward) => {
    await renderTree(language);
    const sector = screen.getByRole('treeitem', { name: 'Sector' });
    sector.focus();
    expect(sector).toHaveAttribute('aria-expanded', 'false');

    await userEvent.keyboard(`{${inward}}`);
    expect(sector).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('treeitem', { name: 'Department' })).toHaveAttribute('aria-level', '3');

    await userEvent.keyboard(`{${inward}}`);
    expect(screen.getByRole('treeitem', { name: 'Department' })).toHaveFocus();

    await userEvent.keyboard(`{${outward}}`);
    expect(sector).toHaveFocus();
    await userEvent.keyboard(`{${outward}}`);
    expect(sector).toHaveAttribute('aria-expanded', 'false');
  });
});

describe('OrgChart', () => {
  it('shows the first levels and expands deeper ones on request', async () => {
    await renderWithProviders(
      <OrgChart
        items={units}
        label="Chart"
        initialDepth={2}
        renderNode={(unit) => <span>{unit.name}</span>}
      />,
    );
    expect(screen.getByRole('figure', { name: 'Chart' })).toBeInTheDocument();
    expect(screen.getByText('Sector')).toBeInTheDocument();
    expect(screen.queryByText('Department')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Show 1 unit' }));
    expect(screen.getByText('Department')).toBeInTheDocument();

    await userEvent.click(screen.getAllByRole('button', { name: 'Hide' })[0] ?? document.body);
    expect(screen.queryByText('Sector')).not.toBeInTheDocument();
  });
});
