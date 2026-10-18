// Contains a render error to the section it happens in. Without this, React unmounts the WHOLE
// page on one throw - slice 02 step 4's E2E saw net worth, accounts and every form vanish because
// the import history's date formatting threw. tests/E2E/SectionFailureTests holds the containment.
import { Component, useId, type ErrorInfo, type ReactNode } from 'react';

type Props = { title: string; children: ReactNode };
type State = { error: Error | null };

/** A class component: React has no hook for catching render errors. */
export class SectionBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // React logs the error too; this ties it to the section that lost it.
    console.error(`Section "${this.props.title}" failed to render.`, error, info.componentStack);
  }

  render() {
    return this.state.error ? <SectionFailed title={this.props.title} error={this.state.error} /> : this.props.children;
  }
}

/** The section's own heading stays, so the page keeps its structure and landmarks. */
function SectionFailed({ title, error }: { title: string; error: Error }) {
  const headingId = useId();
  return (
    <section aria-labelledby={headingId}>
      <h2 id={headingId}>{title}</h2>
      <div role="alert" className="form-error">
        <p>This part of the page could not be shown. The rest of the page still works.</p>
        <p className="hint">{error.message}</p>
      </div>
      <button type="button" onClick={() => window.location.reload()}>Reload the page</button>
    </section>
  );
}
