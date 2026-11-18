import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import SectionTimeline from './SectionTimeline';
import { listening, listeningResult, measurements } from '../test/fixtures';

function renderTimeline(props = {}) {
  const r = listeningResult();
  return render(<SectionTimeline sections={r.Sections} durationMs={182000} measurements={measurements()} listening={listening()} {...props} />);
}

describe('SectionTimeline', () => {
  it('draws one labeled option per section, the silences, and the heard clips', () => {
    const { container } = renderTimeline();
    const bar = screen.getByRole('listbox', { name: /Sections of a 3 min recording/ });
    const options = within(bar).getAllByRole('option');
    expect(options).toHaveLength(3);
    expect(options[0]).toHaveAttribute('aria-label', 'Section 1 of 3, 0:00.0 to 1:00.0, 1 min');
    expect(options[0]).toHaveAttribute('aria-selected', 'true');
    expect(options[2].getAttribute('aria-label')).toMatch(/Has 1 error\./);
    expect(container.querySelectorAll('.section-silence')).toHaveLength(2);
    expect(container.querySelectorAll('.section-cut')).toHaveLength(2);
    expect(container.querySelectorAll('.section-heard')).toHaveLength(3);
    expect(screen.getByText('Heard by the listening model')).toBeInTheDocument();
    expect(screen.getByText('Crowd cheering, then the band starts.')).toBeInTheDocument();
    expect(screen.getByText('Good evening, everybody!')).toBeInTheDocument();
  });

  it('moves between sections with the arrow keys, Home, and End', () => {
    renderTimeline();
    const bar = screen.getByRole('listbox');
    expect(bar).toHaveAttribute('tabindex', '0');
    bar.focus();
    fireEvent.keyDown(bar, { key: 'ArrowRight' });
    expect(screen.getByRole('heading', { name: 'Section 2 of 3' })).toBeInTheDocument();
    expect(within(bar).getAllByRole('option')[1]).toHaveAttribute('aria-selected', 'true');
    expect(bar.getAttribute('aria-activedescendant')).toBe(within(bar).getAllByRole('option')[1].id);
    fireEvent.keyDown(bar, { key: 'End' });
    expect(screen.getByRole('heading', { name: 'Section 3 of 3' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Section 3 summary failed');
    fireEvent.keyDown(bar, { key: 'ArrowRight' });
    expect(screen.getByRole('heading', { name: 'Section 3 of 3' })).toBeInTheDocument();
    fireEvent.keyDown(bar, { key: 'Home' });
    expect(screen.getByRole('heading', { name: 'Section 1 of 3' })).toBeInTheDocument();
    fireEvent.keyDown(bar, { key: 'ArrowLeft' });
    expect(screen.getByRole('heading', { name: 'Section 1 of 3' })).toBeInTheDocument();
  });

  it('selects a section on click and with the previous and next buttons', async () => {
    const user = userEvent.setup();
    renderTimeline();
    await user.click(screen.getAllByRole('option')[1]);
    expect(screen.getByRole('heading', { name: 'Section 2 of 3' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Next section' }));
    expect(screen.getByRole('heading', { name: 'Section 3 of 3' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Next section' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Previous section' }));
    expect(screen.getByText('A mid-tempo song with a sing-along chorus.')).toBeInTheDocument();
  });

  it('explains when sections were not requested or none were found', () => {
    const { unmount } = render(<SectionTimeline sections={null} durationMs={1000} />);
    expect(screen.getByText('Sections were not requested for this analysis.')).toBeInTheDocument();
    unmount();
    render(<SectionTimeline sections={[]} durationMs={1000} />);
    expect(screen.getByText('Section detection found no sections.')).toBeInTheDocument();
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });
});
