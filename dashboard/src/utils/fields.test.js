import { describe, expect, it } from 'vitest';
import { createField, fromFieldSpec, moveItem, toContextItems, toFieldSpec, validateContext, validateFields } from './fields';
import { normalizeBuckets, rangeToParams } from './ranges';
import { buildCurl, redactLargeData } from './snippets';

describe('field model', () => {
  it('converts editable fields to wire FieldSpec, keeping only applicable constraints', () => {
    const f = createField('Integer', { Key: 'dogCount', Prompt: ' How many dogs? ', Minimum: '0', Maximum: '', MaxLength: '5', Default: '2' });
    expect(toFieldSpec(f)).toEqual({ Key: 'dogCount', Type: 'Integer', Prompt: 'How many dogs?', Minimum: 0, Default: 2 });

    const e = createField('Enum', { Key: 'setting', Prompt: 'Where?', Options: ['Indoors', ' Outdoors ', ''], Required: true });
    expect(toFieldSpec(e)).toEqual({ Key: 'setting', Type: 'Enum', Prompt: 'Where?', Required: true, Options: ['Indoors', 'Outdoors'] });

    const l = createField('StringList', { Key: 'colors', Prompt: 'Colors', MaxItems: '3', Default: 'red, green' });
    expect(toFieldSpec(l)).toEqual({ Key: 'colors', Type: 'StringList', Prompt: 'Colors', MaxItems: 3, Default: ['red', 'green'] });
  });

  it('round-trips through fromFieldSpec', () => {
    const spec = { Key: 'obj', Type: 'Object', Prompt: 'Describe', Schema: '{"type":"object"}', Default: { a: 1 } };
    expect(toFieldSpec(fromFieldSpec(spec))).toEqual(spec);
  });

  it('validates keys, prompts, options, and ranges like the server', () => {
    const fields = [
      createField('String', { Key: 'bad key', Prompt: 'x' }),
      createField('Enum', { Key: 'a', Prompt: '' }),
      createField('Integer', { Key: 'A', Prompt: 'p', Minimum: '5', Maximum: '1' }),
      createField('String', { Key: 'Summary', Prompt: 'p' })
    ];
    const { byUid, count } = validateFields(fields, 'Summary');
    expect(byUid[fields[0].uid].Key.key).toBe('fieldBuilder.errors.keyInvalid');
    expect(byUid[fields[1].uid].Prompt.key).toBe('fieldBuilder.errors.promptRequired');
    expect(byUid[fields[1].uid].Options.key).toBe('fieldBuilder.errors.optionsRequired');
    expect(byUid[fields[2].uid].Key.key).toBe('fieldBuilder.errors.keyDuplicate');
    expect(byUid[fields[2].uid].Minimum.key).toBe('fieldBuilder.errors.minAboveMax');
    expect(byUid[fields[3].uid].Key.key).toBe('fieldBuilder.errors.keySummary');
    expect(count).toBe(6);
  });

  it('moves items and normalizes context', () => {
    expect(moveItem([1, 2, 3], 0, 2)).toEqual([2, 3, 1]);
    expect(moveItem([1, 2, 3], 0, -1)).toEqual([1, 2, 3]);
    expect(toContextItems([{ Label: ' Source ', Text: ' a ' }, { Label: '', Text: 'b' }, { Label: 'x', Text: ' ' }])).toEqual([
      { Label: 'Source', Text: 'a' },
      { Text: 'b' }
    ]);
    expect(validateContext([{ Label: 'only a label', Text: '' }])[0].key).toBe('context.errors.textRequired');
  });
});

describe('ranges and snippets', () => {
  it('produces exact bucket counts and fills gaps', () => {
    const params = rangeToParams('hour', Date.UTC(2026, 10, 15, 12, 0, 30));
    expect(params.bucketMinutes).toBe(1);
    const buckets = normalizeBuckets({ Buckets: [{ BucketStartUtc: params.fromUtc, SuccessCount: 3, FailureCount: 1, AverageDurationMs: 2 }] }, params, 60);
    expect(buckets).toHaveLength(60);
    expect(buckets[0]).toMatchObject({ success: 3, failure: 1 });
    expect(buckets[1]).toMatchObject({ success: 0, failure: 0 });
    expect(normalizeBuckets(null, rangeToParams('month'), 120)).toHaveLength(120);
  });

  it('never embeds the key and redacts large base64 data', () => {
    const body = redactLargeData({ Image: { Data: 'A'.repeat(5000) } });
    expect(body.Image.Data).toMatch(/^<base64 data/);
    const curl = buildCurl({ method: 'POST', url: 'http://127.0.0.1:8700/v1.0/image', body });
    expect(curl).toContain('$KEYFRAME_API_KEY');
    expect(curl).not.toContain('AAAAAAAA');
  });
});
