import { acceptCompletion, autocompletion, closeBrackets, closeBracketsKeymap, completionStatus, type Completion as CmCompletion, type CompletionContext } from '@codemirror/autocomplete';
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands';
import { bracketMatching, HighlightStyle, indentOnInput, StreamLanguage, syntaxHighlighting } from '@codemirror/language';
import { csharp } from '@codemirror/legacy-modes/mode/clike';
import { EditorState, Prec, StateEffect, StateField, type Extension } from '@codemirror/state';
import { Decoration, type DecorationSet, drawSelection, EditorView, type KeyBinding, keymap, lineNumbers, placeholder } from '@codemirror/view';
import { tags } from '@lezer/highlight';
import { useEffect, useRef } from 'preact/hooks';
import { api } from '../api/client';
import type { Diagnostic } from '../api/types';

const highlight = HighlightStyle.define([
  { tag: [tags.keyword, tags.modifier, tags.controlKeyword, tags.operatorKeyword], color: 'var(--bool)' },
  { tag: [tags.string, tags.character, tags.special(tags.string)], color: 'var(--str)' },
  { tag: [tags.number, tags.bool, tags.null], color: 'var(--num)' },
  { tag: [tags.comment, tags.lineComment, tags.blockComment], color: 'var(--fg-3)', fontStyle: 'italic' },
  { tag: [tags.typeName, tags.className, tags.namespace], color: 'var(--type)' },
  { tag: [tags.function(tags.variableName), tags.propertyName], color: 'var(--enum)' },
  { tag: tags.meta, color: 'var(--accent)' },
]);

const kindMap: Record<string, string> = {
  method: 'method', prop: 'property', field: 'variable', local: 'variable', const: 'constant', enum: 'enum',
  class: 'class', struct: 'class', interface: 'interface', delegate: 'type', ns: 'namespace', keyword: 'keyword', event: 'variable',
};

/** Roslyn completions from the session (knows console variables and `$n` types). */
async function complete(context: CompletionContext) {
  const word = context.matchBefore(/[\w]*/);
  const afterDot = context.state.sliceDoc(Math.max(0, (word?.from ?? context.pos) - 1), word?.from ?? context.pos) === '.';
  if (!context.explicit && !afterDot && (!word || word.from === word.to)) return null;

  const abort = new AbortController();
  context.addEventListener('abort', () => abort.abort());
  try {
    const result = await api.complete(context.state.doc.toString(), context.pos, abort.signal);
    return {
      from: result.from,
      to: result.to,
      options: result.items.map((i): CmCompletion => ({ label: i.label, type: kindMap[i.kind] ?? 'text', detail: i.detail })),
      validFor: /^\w*$/,
    };
  } catch {
    return null;
  }
}

// ---- diagnostics: wavy underlines with the compiler message as tooltip ----------------------

const setDiagnostics = StateEffect.define<Diagnostic[]>();

const diagnosticsField = StateField.define<DecorationSet>({
  create: () => Decoration.none,
  update(decorations, tr) {
    decorations = decorations.map(tr.changes);
    for (const effect of tr.effects) {
      if (!effect.is(setDiagnostics)) continue;
      const doc = tr.state.doc;
      const at = (line: number, column: number) => {
        const l = doc.line(Math.max(1, Math.min(doc.lines, line)));
        return Math.min(l.to, l.from + column - 1);
      };
      decorations = Decoration.set(effect.value.map(d => {
        const from = at(d.line, d.column);
        const to = Math.max(from + 1, at(d.endLine, d.endColumn));
        return Decoration.mark({ class: `cm-diag cm-diag-${d.severity}`, attributes: { title: `${d.id}: ${d.message}` } }).range(from, Math.min(to, doc.length));
      }).filter(r => r.from < r.to), true);
    }
    if (tr.docChanged) decorations = Decoration.none;
    return decorations;
  },
  provide: f => EditorView.decorations.from(f),
});

export function showDiagnostics(view: EditorView, diagnostics: Diagnostic[]) {
  view.dispatch({ effects: setDiagnostics.of(diagnostics) });
}

export interface CodeEditorProps {
  initial?: string;
  placeholder?: string;
  onChange?: (code: string) => void;
  /** Bindings that run before the defaults (run, history...). */
  keys?: KeyBinding[];
  completion?: boolean;
  numbers?: boolean;
  readOnly?: boolean;
  class?: string;
  onReady?: (view: EditorView) => void;
}

export function CodeEditor(props: CodeEditorProps) {
  const host = useRef<HTMLDivElement>(null);
  const latest = useRef(props);
  latest.current = props;

  useEffect(() => {
    const extensions: Extension[] = [
      history(),
      drawSelection(),
      indentOnInput(),
      bracketMatching(),
      closeBrackets(),
      StreamLanguage.define(csharp),
      syntaxHighlighting(highlight),
      diagnosticsField,
      EditorState.tabSize.of(4),
      EditorView.lineWrapping,
      EditorView.contentAttributes.of({ spellcheck: 'false', autocapitalize: 'off', autocorrect: 'off' }),
      EditorView.updateListener.of(u => u.docChanged && latest.current.onChange?.(u.state.doc.toString())),
      // Tab accepts a completion; while the list is loading or just changed it does nothing rather than indent.
      Prec.highest(keymap.of([{ key: 'Tab', run: v => completionStatus(v.state) != null && (acceptCompletion(v), true) }])),
      Prec.high(keymap.of(props.keys ?? [])),
      keymap.of([...closeBracketsKeymap, ...defaultKeymap, ...historyKeymap, indentWithTab]),
    ];
    if (props.placeholder) extensions.push(placeholder(props.placeholder));
    if (props.completion) extensions.push(autocompletion({ override: [complete], icons: true, maxRenderedOptions: 200 }));
    if (props.numbers) extensions.push(lineNumbers());
    if (props.readOnly) extensions.push(EditorState.readOnly.of(true));

    const view = new EditorView({ parent: host.current!, state: EditorState.create({ doc: props.initial ?? '', extensions }) });
    props.onReady?.(view);
    return () => view.destroy();
  }, []);

  return <div class={`code ${props.class ?? ''}`} ref={host} />;
}
