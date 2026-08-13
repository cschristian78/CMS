import { Component, type ErrorInfo, type ReactNode } from 'react';

type Props = {
  children: ReactNode;
};

type State = {
  error: Error | null;
};

/** Keeps the shell/sidebar alive when a page throws during navigation. */
export default class RouteErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error('Route render failed', error, info);
  }

  private clearError = () => {
    this.setState({ error: null });
  };

  render() {
    if (this.state.error) {
      return (
        <div className="card narrow-card" role="alert">
          <h2>Something went wrong</h2>
          <p className="field-help">{this.state.error.message || 'This page failed to render.'}</p>
          <div className="page-header-actions">
            <button type="button" className="primary-button" onClick={this.clearError}>
              Try again
            </button>
            <a className="secondary-button" href="/" onClick={this.clearError}>
              Go to Home
            </a>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
