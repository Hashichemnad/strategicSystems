// Shared UI kit (CLAUDE.md §10): every screen builds from these, never re-invents them.
export { Button, buttonVariants, type ButtonProps } from './button';
export { ConfirmDialog, Dialog, DialogClose, DialogContent, DialogTrigger } from './dialog';
export { DatePicker } from './date-picker';
export {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from './dropdown-menu';
export { EmptyState, Skeleton, StatusChip, type Tone } from './feedback';
export { FormField, Label } from './form-field';
export { Input, Textarea } from './input';
export { EmbeddedPage, PageHeader } from './page-header';
export { RichTextEditor, type RichTextEditorProps } from './rich-text-field';
export { RichTextView } from './rich-text-view';
export { isBlank as isRichTextBlank, isHtml, sanitizeHtml, toPlainText } from './rich-text';
export { ScrollRegion } from './scroll-region';
export { Select, type SelectOption } from './select';
export { StageStepper, type Stage } from './stage-stepper';
export { RecordLayout, type Highlight, type RecordLayoutProps, type RecordSection } from './record-layout';
export {
  RecordSectionsDrawer,
  RecordSideMenu,
  useNarrowViewport,
  type RecordMenuItem,
} from './record-side-menu';
export { ListViewToggle, SplitCard, SplitPreview, SplitView, useListView } from './split-view';
export { StatusBanner } from './status-banner';
export {
  Pagination,
  SortableHeader,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  type SortDirection,
} from './table';
export { Tabs, TabsContent, TabsList, TabsTrigger } from './tabs';
export { ToastProvider, useToast } from './toast';
export { Wizard, type WizardStep } from './wizard';
export { Avatar } from './avatar';
export { CopyField } from './copy-field';
export {
  CommandSearch,
  type CommandSearchGroup,
  type CommandSearchItem,
  type CommandSearchProps,
} from './command-search';
export { FileDrop } from './file-drop';
export { OrgChart } from './org-chart';
export { OtpInput } from './otp-input';
export { Checkbox, Switch } from './toggle';
export { buildTree, type FlatNode, type TreeNode } from './tree';
export { TreeView } from './tree-view';
export * from './charts';
export * from './dashboard';
export {
  Gantt,
  exportGanttPdf,
  exportGanttPng,
  type GanttDependency,
  type GanttProps,
  type GanttRow,
} from './gantt';
