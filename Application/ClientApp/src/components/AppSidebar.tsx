import { Link, useLocation } from 'react-router-dom';

const iconProps = {
  width: 22,
  height: 22,
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
};

function HomeIcon() {
  return (
    <svg {...iconProps} aria-hidden="true">
      <path d="M3 10.5 12 3l9 7.5V20a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1v-9.5Z" />
    </svg>
  );
}

function PlusIcon() {
  return (
    <svg {...iconProps} aria-hidden="true">
      <path d="M12 5v14M5 12h14" />
    </svg>
  );
}

function DashboardIcon() {
  return (
    <svg {...iconProps} aria-hidden="true">
      <rect x="3" y="3" width="7" height="7" rx="1" />
      <rect x="14" y="3" width="7" height="7" rx="1" />
      <rect x="3" y="14" width="7" height="7" rx="1" />
      <rect x="14" y="14" width="7" height="7" rx="1" />
    </svg>
  );
}

function CatAiIcon() {
  return (
    <svg {...iconProps} aria-hidden="true">
      <path d="M12 3 9.5 8.5 3 9l5 4.5L6.5 21 12 17l5.5 4-1.5-7.5L21 9l-6.5-.5L12 3Z" />
    </svg>
  );
}

function AdminIcon() {
  return (
    <svg {...iconProps} aria-hidden="true">
      <circle cx="12" cy="12" r="3" />
      <path d="M12 1v2M12 21v2M4.22 4.22l1.42 1.42M18.36 18.36l1.42 1.42M1 12h2M21 12h2M4.22 19.78l1.42-1.42M18.36 5.64l1.42-1.42" />
    </svg>
  );
}

interface NavItemDef {
  to: string;
  label: string;
  icon: JSX.Element;
  isActive: (pathname: string) => boolean;
}

const navItems: NavItemDef[] = [
  { to: '/', label: 'Home', icon: <HomeIcon />, isActive: (p) => p === '/' },
  {
    to: '/projects/new',
    label: 'New Project',
    icon: <PlusIcon />,
    isActive: (p) => p === '/projects/new',
  },
  {
    to: '/dashboard',
    label: 'Dashboard',
    icon: <DashboardIcon />,
    isActive: (p) => p === '/dashboard' || p.startsWith('/dashboard/'),
  },
  {
    to: '/catai',
    label: 'CatAI',
    icon: <CatAiIcon />,
    isActive: (p) => p === '/catai' || p.startsWith('/catai/'),
  },
  {
    to: '/admin',
    label: 'Admin',
    icon: <AdminIcon />,
    isActive: (p) => p === '/admin' || p.startsWith('/admin/'),
  },
];

export default function AppSidebar() {
  const { pathname } = useLocation();

  return (
    <aside className="app-sidebar" aria-label="Main navigation">
      <div className="app-sidebar-brand">
        <Link to="/" className="app-sidebar-brand-link" title="Client Management System">
          <span className="app-sidebar-brand-mark" aria-hidden="true">
            <span className="logo-accent">C</span>
            <span className="logo-rest">MS</span>
          </span>
        </Link>
        <span className="app-sidebar-brand-sub">Client Mgmt</span>
      </div>
      <nav className="sidebar-nav">
        {navItems.map((item) => {
          const active = item.isActive(pathname);
          return (
            <Link
              key={item.to}
              to={item.to}
              className={'sidebar-nav-item' + (active ? ' active' : '')}
              title={item.label}
              aria-current={active ? 'page' : undefined}
            >
              <span className="sidebar-nav-icon">{item.icon}</span>
              <span className="sidebar-nav-label">{item.label}</span>
            </Link>
          );
        })}
      </nav>
    </aside>
  );
}
