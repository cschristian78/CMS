import { Link } from 'react-router-dom';
import { APP_BUILD_NUMBER } from '../appBuild';
import { setCatAppMode } from '../catAppMode';

export default function LandingPage() {
  return (
    <div className="app-hub">
      <div className="app-hub-apps">
        <p className="app-hub-eyebrow">Select Application</p>
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

          <Link to="/catai" className="app-hub-tile app-hub-tile--catai">
            <span className="app-hub-tile-code" aria-hidden="true">
              <span className="logo-accent">C</span>
              <span className="logo-rest">ASI</span>
            </span>
            <span className="app-hub-tile-title">Catastrophe Analytics Super Intelligence</span>
            <span className="app-hub-tile-desc">
              Ask questions about clients, projects, and work in natural language.
            </span>
            <span className="app-hub-tile-cta">Open CASI</span>
          </Link>
        </nav>

        <p className="app-hub-help">
          <a href="/api/app/help-manual" target="_blank" rel="noopener noreferrer">
            Get Help
          </a>
        </p>
        <p className="app-hub-build" aria-label="Application build">
          Build {APP_BUILD_NUMBER}
        </p>
      </div>
    </div>
  );
}
