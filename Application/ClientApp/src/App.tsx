import { useState } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import AppSidebar from './components/AppSidebar';
import BackgroundLayer from './components/BackgroundLayer';
import RouteErrorBoundary from './components/RouteErrorBoundary';
import LandingPage from './pages/LandingPage';
import HomePage from './pages/HomePage';
import TmsHomePage from './pages/TmsHomePage';
import TmsDashboardPage from './pages/TmsDashboardPage';
import ClientPage from './pages/ClientPage';
import ProjectPage from './pages/ProjectPage';
import ProjectSummaryPage from './pages/ProjectSummaryPage';
import AdminPage from './pages/AdminPage';
import CatAiPage from './pages/CatAiPage';
import DashboardPage from './pages/DashboardPage';

export type FlashFn = (kind: 'success' | 'error', message: string) => void;

export default function App() {
  const [flash, setFlash] = useState<{ kind: 'success' | 'error'; text: string } | null>(null);
  const location = useLocation();
  const clientWorkflow = location.pathname.startsWith('/clients');
  const projectWide = location.pathname.startsWith('/projects');
  const adminWide = location.pathname === '/admin';
  const hubWide = location.pathname === '/';
  const cmsWide = location.pathname === '/cms';
  const tmsWide =
    location.pathname === '/tms' ||
    location.pathname.startsWith('/tms/');
  const dashboardWide =
    location.pathname === '/dashboard' || location.pathname === '/tms/dashboard';

  const flashFn: FlashFn = (kind, message) => {
    setFlash({ kind, text: message });
  };

  const mainClass =
    'page-shell' +
    (clientWorkflow ? ' page-shell--client-workflow' : '') +
    (adminWide ? ' page-shell--admin-wide' : '') +
    (hubWide ? ' page-shell--hub-wide' : '') +
    (cmsWide ? ' page-shell--home-wide' : '') +
    (tmsWide ? ' page-shell--hub-wide' : '') +
    (dashboardWide ? ' page-shell--dashboard-wide' : '') +
    (projectWide ? ' page-shell--project-wide' : '');

  return (
    <div className="app-frame">
      <BackgroundLayer />
      <AppSidebar />

      <div className="app-content">
        <main className={mainClass}>
          {flash && (
            <div className={flash.kind === 'success' ? 'alert alert-success' : 'alert alert-error'}>
              {flash.text}
            </div>
          )}

          {/* key=pathname remounts the boundary so navigating away clears a prior page error */}
          <RouteErrorBoundary key={location.pathname}>
            <Routes>
              <Route path="/" element={<LandingPage />} />
              <Route path="/cms" element={<HomePage flash={flashFn} />} />
              <Route path="/tms" element={<TmsHomePage flash={flashFn} />} />
              <Route path="/tms/dashboard" element={<TmsDashboardPage />} />
              <Route path="/dashboard" element={<DashboardPage />} />
              <Route path="/clients/new" element={<ClientPage flash={flashFn} />} />
              <Route path="/clients/:id" element={<ClientPage flash={flashFn} />} />
              <Route path="/projects/new" element={<ProjectPage flash={flashFn} />} />
              <Route path="/projects/:id/summary" element={<ProjectSummaryPage />} />
              <Route path="/projects/:id" element={<ProjectPage flash={flashFn} />} />
              <Route path="/catai" element={<CatAiPage />} />
              <Route path="/admin" element={<AdminPage flash={flashFn} />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </RouteErrorBoundary>
        </main>
      </div>
    </div>
  );
}
