import { indexSections, sectionAt, sectionText } from './preview-section.mjs';
import { EditorState, Compartment } from '@codemirror/state';
import { EditorView, keymap, lineNumbers, highlightActiveLine, drawSelection } from '@codemirror/view';
import { defaultKeymap, history, historyKeymap, undo, redo } from '@codemirror/commands';
import { searchKeymap, search, openSearchPanel } from '@codemirror/search';
import { StreamLanguage, syntaxHighlighting, HighlightStyle } from '@codemirror/language';
import { tags } from '@lezer/highlight';

// Highlighting is deliberately conservative; the shared .NET parser remains authoritative.
const chord = /(?:[A-G](?:#|b)?(?:m(?:aj|in)?|dim|aug|sus|add)?\d*(?:[#b]\d+)*(?:\/[A-G][#b]?)?|N\.?C\.?)/;
const chordLine = new RegExp('^\\s*(?:' + chord.source + '[\\s|:.,()-]+)*' + chord.source + '[\\s|:.,()-]*$');
const songLanguage = StreamLanguage.define({
	startState: () => ({ chordLine: false }),
	token(stream, state) {
		if (stream.sol()) state.chordLine = chordLine.test(stream.string);
		if (stream.sol() && stream.match(/\s*#.*/)) return 'comment';
		if (stream.match(/\{(?:start_of_|end_of_|soc|eoc|sov|eov)[^}]*\}/)) return 'heading';
		if (stream.match(/\{[^}\n]*\}/)) return 'meta';
		if (stream.match(/\[[^\]\n]+\]/)) return 'atom';
		if (state.chordLine && stream.match(chord)) return 'atom';
		stream.next();
		return null;
	}
});
const highlights = HighlightStyle.define([
	{ tag: tags.atom, class: 'cm-chord' }, { tag: tags.meta, class: 'cm-directive' },
	{ tag: tags.comment, class: 'cm-comment' }, { tag: tags.heading, class: 'cm-section' }
]);
const readOnly = new Compartment();
const theme = new Compartment();
let initialText = '';
let initialDoc;
let lineEnding = '\n';
let version = 0;
let previewActive = false;
let previewIndex, previewDoc, previewRange;
function currentSection(state) {
	if (previewDoc !== state.doc) { previewDoc = state.doc; previewIndex = indexSections(state.doc); }
	return sectionAt(previewIndex, state.selection.main.head);
}
function showSearch(editor, replace = false) {
	openSearchPanel(editor);
	const panel = editor.dom.querySelector('.cm-search');
	panel?.querySelector(replace ? '[name=replace]' : '[name=search]')?.focus();
	return true;
}
const notify = type => window.chrome?.webview?.postMessage(type);
const darkTheme = EditorView.theme({
	'&': { color: '#ededed', backgroundColor: '#202020' },
	'.cm-content': { caretColor: '#ededed' },
	'.cm-cursor': { borderLeftColor: '#ededed' },
	'.cm-gutters': { backgroundColor: '#292929', color: '#bdbdbd', border: 'none' },
	'.cm-activeLine': { backgroundColor: '#ffffff0c' },
	'.cm-selectionBackground, &.cm-focused .cm-selectionBackground': { backgroundColor: '#315273' },
	'.cm-panels': { backgroundColor: '#292929', color: '#ededed' }
}, { dark: true });
const dark = matchMedia('(prefers-color-scheme: dark)');
function makeState(text) {
	return EditorState.create({ doc: text, extensions: [
		EditorState.phrases.of({ next: 'Next', previous: 'Previous', all: 'All',
			'match case': 'Match case', regexp: 'Regexp', 'by word': 'By word',
			replace: 'Replace', 'replace all': 'Replace all', close: 'Close' }),
		lineNumbers(), history(), drawSelection(), highlightActiveLine(), search(), songLanguage,
		syntaxHighlighting(highlights), keymap.of([{ key: 'Mod-y', run: redo }, { key: 'Mod-Shift-z', run: redo }, { key: 'Mod-h', run: editor => showSearch(editor, true) }, { key: 'Mod-f', run: editor => showSearch(editor) }, ...defaultKeymap, ...historyKeymap, ...searchKeymap]),
		EditorView.contentAttributes.of({ 'aria-label': 'Song source', spellcheck: 'false' }),
		readOnly.of(EditorState.readOnly.of(false)), theme.of(dark.matches ? darkTheme : []),
		EditorView.updateListener.of(update => {
			if (update.docChanged) { version++; notify('changed'); }
			if (previewActive && (update.docChanged || update.selectionSet)) {
				const range = currentSection(update.state);
				if (update.docChanged || range !== previewRange) { previewRange = range; notify('preview'); }
			}
		})
	] });
}
const view = new EditorView({ state: makeState(''), parent: document.querySelector('#editor') });
dark.addEventListener('change', () => view.dispatch({ effects: theme.reconfigure(dark.matches ? darkTheme : []) }));
window.chordBookEditor = {
	load(text) {
		initialText = text;
		const counts = new Map();
		for (const match of text.matchAll(/\r\n|\r|\n/g)) counts.set(match[0], (counts.get(match[0]) || 0) + 1);
		lineEnding = [...counts].sort((a, b) => b[1] - a[1])[0]?.[0] || '\n';
		view.setState(makeState(text)); initialDoc = view.state.doc; version = 0; previewIndex = previewDoc = previewRange = undefined;
	},
	getText() { return view.state.doc.eq(initialDoc) ? initialText : view.state.doc.toString().replace(/\n/g, lineEnding); },
	getVersion() { return version; },
	getPreviewText() { const range = currentSection(view.state); return sectionText(view.state.doc, previewIndex, range); },
	setPreviewEnabled(enabled) { previewActive = enabled; if (enabled) previewRange = currentSection(view.state); },
	setUiFont(family, size) {
		document.documentElement.style.setProperty('--editor-ui-font-family', JSON.stringify(family) + ', system-ui');
		document.documentElement.style.setProperty('--editor-ui-font-size', size + 'px');
	},
	replaceText(text) { view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text } }); },
	setReadOnly(value) { view.dispatch({ effects: readOnly.reconfigure([EditorState.readOnly.of(value), EditorView.editable.of(!value)]) }); },
	undo() { view.focus(); return undo(view); }, redo() { view.focus(); return redo(view); },
	search(replace = false) { showSearch(view, replace); }, focus() { view.focus(); }
};
window.chordBookEditor.load('');
notify('ready');
