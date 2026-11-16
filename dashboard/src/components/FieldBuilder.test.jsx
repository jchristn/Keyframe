import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import FieldBuilder from './FieldBuilder';
import { createField, toFieldSpec, validateFields } from '../utils/fields';

function Harness({ initial = [], withErrors = false, onState = () => {} }) {
  const [fields, setFields] = useState(initial);
  onState(fields);
  const errors = withErrors ? validateFields(fields).byUid : {};
  return <FieldBuilder fields={fields} onChange={setFields} errors={errors} />;
}

describe('FieldBuilder', () => {
  it('adds a field of the chosen type and shows its constraints', async () => {
    const user = userEvent.setup();
    let state = [];
    render(<Harness onState={(f) => (state = f)} />);
    expect(screen.getByText(/No fields yet/)).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Type for the new field'), 'Integer');
    await user.click(screen.getByRole('button', { name: 'Add field' }));
    const rows = screen.getAllByTestId('field-row');
    expect(rows).toHaveLength(1);
    expect(within(rows[0]).getByLabelText('Minimum')).toBeInTheDocument();
    expect(within(rows[0]).getByLabelText('Maximum')).toBeInTheDocument();
    expect(within(rows[0]).queryByLabelText('Options')).not.toBeInTheDocument();

    await user.type(within(rows[0]).getByLabelText(/^Key/), 'dogCount');
    await user.type(within(rows[0]).getByLabelText(/^Prompt/), 'How many dogs?');
    await user.type(within(rows[0]).getByLabelText('Minimum'), '0');
    expect(toFieldSpec(state[0])).toEqual({ Key: 'dogCount', Type: 'Integer', Prompt: 'How many dogs?', Minimum: 0 });
  });

  it('switches constraints when the type changes and edits Enum options as chips', async () => {
    const user = userEvent.setup();
    let state = [];
    render(<Harness initial={[createField('String', { Key: 'setting', Prompt: 'Where?' })]} onState={(f) => (state = f)} />);
    const row = screen.getByTestId('field-row');
    expect(within(row).getByLabelText('Max length')).toBeInTheDocument();

    await user.selectOptions(within(row).getByLabelText('Type'), 'Enum');
    expect(within(row).queryByLabelText('Max length')).not.toBeInTheDocument();
    const options = within(row).getByLabelText(/^Options/);
    await user.type(options, 'Indoors{Enter}Outdoors,');
    expect(state[0].Options).toEqual(['Indoors', 'Outdoors']);
    await user.click(within(row).getByRole('button', { name: 'Remove option Indoors' }));
    expect(state[0].Options).toEqual(['Outdoors']);
  });

  it('reorders, duplicates, and removes fields', async () => {
    const user = userEvent.setup();
    let state = [];
    render(
      <Harness
        initial={[createField('Boolean', { Key: 'a', Prompt: 'A?' }), createField('Boolean', { Key: 'b', Prompt: 'B?' })]}
        onState={(f) => (state = f)}
      />
    );
    let rows = screen.getAllByTestId('field-row');
    expect(within(rows[0]).getByRole('button', { name: 'Move up' })).toBeDisabled();
    await user.click(within(rows[0]).getByRole('button', { name: 'Move down' }));
    expect(state.map((f) => f.Key)).toEqual(['b', 'a']);

    rows = screen.getAllByTestId('field-row');
    await user.click(within(rows[1]).getByRole('button', { name: 'Duplicate field' }));
    expect(state.map((f) => f.Key)).toEqual(['b', 'a', 'a_copy']);
    expect(new Set(state.map((f) => f.uid)).size).toBe(3);

    rows = screen.getAllByTestId('field-row');
    await user.click(within(rows[0]).getByRole('button', { name: 'Remove field' }));
    expect(state.map((f) => f.Key)).toEqual(['a', 'a_copy']);
  });

  it('renders validation errors inline next to the field', () => {
    render(<Harness withErrors initial={[createField('Enum', { Key: 'bad key', Prompt: '' })]} />);
    const row = screen.getByTestId('field-row');
    expect(within(row).getByText(/Use 1 to 64 letters/)).toBeInTheDocument();
    expect(within(row).getByText('A prompt is required.')).toBeInTheDocument();
    expect(within(row).getByText('Enum fields need at least one option.')).toBeInTheDocument();
    expect(within(row).getByLabelText(/^Key/)).toHaveAttribute('aria-invalid', 'true');
  });
});
