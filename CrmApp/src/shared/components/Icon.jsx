import React from 'react'
import {
  AlertCircle,
  AlertTriangle,
  Archive,
  ArrowUpRight,
  BarChart2,
  Bell,
  Calendar,
  Check,
  CheckSquare,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  CircleDollarSign,
  CircleHelp,
  Clock,
  Columns3,
  Copy,
  DollarSign,
  Download,
  ExternalLink,
  Eye,
  EyeOff,
  FileText,
  Filter,
  Inbox,
  Info,
  Kanban,
  ListFilter,
  Lock,
  LogOut,
  Mail,
  MailOpen,
  Menu,
  MessageCircle,
  MessageSquare,
  MoreHorizontal,
  MoreVertical,
  Pencil,
  Phone,
  Pin,
  Plus,
  RefreshCw,
  RotateCw,
  Search,
  Send,
  Settings,
  Share2,
  SlidersHorizontal,
  Sparkles,
  Star,
  Table,
  Tag,
  Trash2,
  Upload,
  User,
  Users,
  X,
  Target,
  PartyPopper,
  ClipboardList,
  FolderOpen,
  Folder,
} from 'lucide-react'

export const ICON_MAP = {
  // Tên cũ đã dùng trong CrmApp
  inbox: Inbox,
  customers: Users,
  tasks: CheckSquare,
  settings: Settings,
  menu: Menu,
  close: X,
  logout: LogOut,
  search: Search,
  plus: Plus,
  send: Send,
  user: User,
  tag: Tag,

  // Tên mới thay thế emoji và phục vụ CRM UI
  table: Table,
  kanban: Kanban,
  filter: Filter,
  refresh: RefreshCw,
  users: Users,
  phone: Phone,
  mail: Mail,
  calendar: Calendar,
  clock: Clock,
  pin: Pin,
  'inbox-empty': MailOpen,
  chart: BarChart2,
  star: Star,
  trash: Trash2,
  edit: Pencil,
  archive: Archive,
  'more-vertical': MoreVertical,
  check: Check,
  x: X,
  sparkles: Sparkles,
  note: FileText,
  bell: Bell,
  alert: AlertTriangle,
  lock: Lock,
  'external-link': ExternalLink,
  message: MessageSquare,
  comment: MessageCircle,
  money: CircleDollarSign,
  'chevron-down': ChevronDown,
  'chevron-up': ChevronUp,
  'chevron-left': ChevronLeft,
  'chevron-right': ChevronRight,

  // Tiện ích & alias mở rộng
  info: Info,
  help: CircleHelp,
  'circle-help': CircleHelp,
  eye: Eye,
  'eye-off': EyeOff,
  copy: Copy,
  share: Share2,
  download: Download,
  upload: Upload,
  dollar: DollarSign,
  pencil: Pencil,
  'file-text': FileText,
  'check-square': CheckSquare,
  'more-horizontal': MoreHorizontal,
  'arrow-up-right': ArrowUpRight,
  'rotate-cw': RotateCw,
  columns: Columns3,
  'list-filter': ListFilter,
  sliders: SlidersHorizontal,
  'alert-circle': AlertCircle,
  'alert-triangle': AlertTriangle,
  'message-square': MessageSquare,
  'message-circle': MessageCircle,
  'mail-open': MailOpen,
  'bar-chart': BarChart2,
  target: Target,
  'party-popper': PartyPopper,
  celebrate: PartyPopper,
  clipboard: ClipboardList,
  'clipboard-list': ClipboardList,
  folder: Folder,
  'folder-open': FolderOpen,
}

export const Icon = ({
  name,
  size = 18,
  color = 'currentColor',
  className = '',
  strokeWidth = 2,
  title,
  ...props
}) => {
  let SelectedIcon = ICON_MAP[name]

  if (!SelectedIcon) {
    if (typeof import.meta !== 'undefined' && import.meta.env?.DEV) {
      console.warn(`[Icon] Unknown icon name "${name}", falling back to CircleHelp`)
    }
    SelectedIcon = CircleHelp
  }

  const hasAccessibleLabel = Boolean(title || props['aria-label'])
  const role = props.role ?? (hasAccessibleLabel ? 'img' : undefined)
  const ariaHidden = props['aria-hidden'] ?? (hasAccessibleLabel ? undefined : true)

  return (
    <SelectedIcon
      size={size}
      color={color}
      strokeWidth={strokeWidth}
      className={className}
      role={role}
      aria-hidden={ariaHidden}
      title={title}
      {...props}
    />
  )
}

export default Icon
