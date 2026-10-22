import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { renderWithProviders } from '@/test/render';
import {
  Button,
  ConfirmDialog,
  DatePicker,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  SortableHeader,
  StageStepper,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableHead,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  useToast,
  Wizard,
} from '@/ui';

const stages = [
  { key: 'request', label: 'Request' },
  { key: 'charter', label: 'Charter' },
  { key: 'closure', label: 'Closure' },
];

describe('Button', () => {
  it('is a non-submitting button by default and disabled while loading', async () => {
    const onClick = vi.fn();
    await renderWithProviders(
      <>
        <Button onClick={onClick}>Save</Button>
        <Button loading>Busy</Button>
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(onClick).toHaveBeenCalledOnce();
    expect(screen.getByRole('button', { name: 'Save' })).toHaveAttribute('type', 'button');
    expect(screen.getByRole('button', { name: 'Busy' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Busy' })).toHaveAttribute('aria-busy', 'true');
  });
});

describe('FormField', () => {
  it('links label, hint and error to the control', async () => {
    await renderWithProviders(
      <FormField label="Title" hint="Short and clear" error="This field is required." required>
        <Input />
      </FormField>,
    );
    const input = screen.getByLabelText(/Title/);
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAccessibleDescription('Short and clear This field is required.');
    expect(screen.getByRole('alert')).toHaveTextContent('This field is required.');
  });
});

describe('StageStepper', () => {
  it('marks earlier stages completed and the current one with aria-current', async () => {
    await renderWithProviders(<StageStepper stages={stages} current="charter" />);
    const items = within(screen.getByRole('navigation', { name: 'Progress' })).getAllByRole('listitem');
    expect(items[0]).toHaveTextContent('Completed');
    expect(items[1]).toHaveAttribute('aria-current', 'step');
    expect(items[2]).toHaveTextContent('Not started');
  });

  it('labels stages in Arabic', async () => {
    await renderWithProviders(<StageStepper stages={stages} current="request" />, { language: 'ar' });
    expect(screen.getByRole('navigation', { name: 'مراحل التقدم' })).toBeInTheDocument();
    expect(document.documentElement).toHaveAttribute('dir', 'rtl');
  });
});

describe('StatusBanner', () => {
  it('shows the current status, who acts next and the available actions', async () => {
    await renderWithProviders(
      <StatusBanner
        title="Waiting for approval"
        description="Next: Director"
        actions={<Button>Approve</Button>}
      />,
    );
    const banner = screen.getByRole('status');
    expect(banner).toHaveTextContent('Waiting for approval');
    expect(banner).toHaveTextContent('Next: Director');
    expect(within(banner).getByRole('button', { name: 'Approve' })).toBeInTheDocument();
  });
});

describe('Wizard', () => {
  it('blocks moving forward while a step is invalid and finishes on the last step', async () => {
    const onFinish = vi.fn();
    const onSaveDraft = vi.fn();
    let valid = false;
    await renderWithProviders(
      <Wizard
        steps={[
          { key: 'a', title: 'Basics', content: <p>first</p>, validate: () => valid },
          { key: 'b', title: 'Review', content: <p>second</p> },
        ]}
        onFinish={onFinish}
        onSaveDraft={onSaveDraft}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    expect(screen.getByText('first')).toBeInTheDocument();

    valid = true;
    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    expect(screen.getByText('second')).toBeInTheDocument();
    expect(screen.getByText('Step 2 of 2')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Save as draft' }));
    expect(onSaveDraft).toHaveBeenCalledOnce();
    await userEvent.click(screen.getByRole('button', { name: 'Finish' }));
    expect(onFinish).toHaveBeenCalledOnce();

    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByText('first')).toBeInTheDocument();
  });
});

describe('Dialog', () => {
  it('confirm dialog runs the action and cancel closes it', async () => {
    const onConfirm = vi.fn();
    const onOpenChange = vi.fn();
    await renderWithProviders(
      <ConfirmDialog
        open
        onOpenChange={onOpenChange}
        title="Deactivate?"
        description="Nothing is deleted."
        confirmLabel="Deactivate"
        onConfirm={onConfirm}
        destructive
      />,
    );
    const dialog = screen.getByRole('dialog', { name: 'Deactivate?' });
    expect(dialog).toHaveAccessibleDescription('Nothing is deleted.');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Deactivate' }));
    expect(onConfirm).toHaveBeenCalledOnce();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});

describe('Tabs', () => {
  it('switches panels with the keyboard', async () => {
    await renderWithProviders(
      <Tabs defaultValue="one">
        <TabsList>
          <TabsTrigger value="one">One</TabsTrigger>
          <TabsTrigger value="two">Two</TabsTrigger>
        </TabsList>
        <TabsContent value="one">Panel one</TabsContent>
        <TabsContent value="two">Panel two</TabsContent>
      </Tabs>,
    );
    await userEvent.click(screen.getByRole('tab', { name: 'One' }));
    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: 'Two' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tabpanel')).toHaveTextContent('Panel two');
  });
});

describe('Table', () => {
  it('announces sort order and pages with mirrored controls', async () => {
    const onSort = vi.fn();
    const onPageChange = vi.fn();
    await renderWithProviders(
      <>
        <Table>
          <TableHead>
            <tr>
              <SortableHeader direction="desc" onSort={onSort}>
                Name
              </SortableHeader>
            </tr>
          </TableHead>
          <TableBody />
        </Table>
        <Pagination page={1} totalPages={3} totalCount={27} onPageChange={onPageChange} />
      </>,
    );
    expect(screen.getByRole('columnheader')).toHaveAttribute('aria-sort', 'descending');
    await userEvent.click(screen.getByRole('button', { name: /Name/ }));
    expect(onSort).toHaveBeenCalledOnce();
    expect(screen.getByText('27 results')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Previous page' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Next page' }));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });
});

describe('Select', () => {
  it('shows the selected option label', async () => {
    await renderWithProviders(
      <Select
        aria-label="Status"
        value="active"
        onValueChange={() => undefined}
        options={[
          { value: 'active', label: 'Active' },
          { value: 'inactive', label: 'Inactive' },
        ]}
      />,
    );
    expect(screen.getByRole('combobox', { name: 'Status' })).toHaveTextContent('Active');
  });
});

describe('DatePicker', () => {
  it('opens a calendar and returns an ISO calendar date', async () => {
    const onChange = vi.fn();
    await renderWithProviders(<DatePicker value="2026-10-22" onChange={onChange} />);
    await userEvent.click(screen.getByRole('button', { name: /2026/ }));
    await userEvent.click(screen.getByRole('button', { name: /15 October 2026/ }));
    expect(onChange).toHaveBeenCalledWith('2026-10-15');
  });
});

describe('Toast', () => {
  function Trigger() {
    const toast = useToast();
    return <Button onClick={() => toast({ title: 'Saved', description: 'All good' })}>Go</Button>;
  }

  it('shows a toast', async () => {
    await renderWithProviders(<Trigger />);
    await userEvent.click(screen.getByRole('button', { name: 'Go' }));
    expect(await screen.findByText('Saved')).toBeInTheDocument();
    expect(screen.getByText('All good')).toBeInTheDocument();
  });
});

describe('Page furniture', () => {
  it('renders header, chips, empty state and an aria-hidden skeleton', async () => {
    const { container } = await renderWithProviders(
      <>
        <PageHeader
          title="Projects"
          description="All projects"
          meta={<StatusChip tone="success">Active</StatusChip>}
          actions={<Button>New</Button>}
        />
        <EmptyState title="No projects" description="Create one" />
        <Skeleton className="h-4" />
      </>,
    );
    expect(screen.getByRole('heading', { level: 1, name: 'Projects' })).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'No projects' })).toBeInTheDocument();
    expect(container.querySelector('.animate-pulse')).toHaveAttribute('aria-hidden', 'true');
  });
});
