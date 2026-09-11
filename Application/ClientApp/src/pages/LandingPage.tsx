import { Link } from 'react-router-dom';
import { setCatAppMode } from '../catAppMode';

export default function LandingPage() {
  return (
    <div className="app-hub">
      <header className="app-hub-brand">
        <p className="app-hub-brand-mark" aria-label="Cat Management System">
          <span className="logo-accent">Cat</span>
          <span className="app-hub-brand-rest"> Management System</span>
        </p>
        <p className="app-hub-lede">Choose an application to continue.</p>
      </header>

      <nav className="app-hub-tiles" aria-label="Applications">
        <Link
          to="/cms"
          className="app-hub-tile app-hub-tile--cms"
          onClick={() => setCatAppMode('cm')}
        >
          <span className="app-hub-tile-code" aria-hidden="true">
            <span className="logo-accent">C</span>
            <span className="logo-rest">M</span>
          </span>
          <span className="app-hub-tile-title">Client Management</span>
          <span className="app-hub-tile-desc">
            Clients, projects, services, reports, and advisory workflows.
          </span>
          <span className="app-hub-tile-cta">Open CM</span>
        </Link>

        <Link
          to="/tms"
          className="app-hub-tile app-hub-tile--tms"
          onClick={() => setCatAppMode('tm')}
        >
          <span className="app-hub-tile-code" aria-hidden="true">
            <span className="logo-accent">T</span>
            <span className="logo-rest">M</span>
          </span>
          <span className="app-hub-tile-title">Time Management</span>
          <span className="app-hub-tile-desc">
            Time tracking and allocation across projects and analysts.
          </span>
          <span className="app-hub-tile-cta">Open TM</span>
        </Link>
      </nav>
    </div>
  );
}
