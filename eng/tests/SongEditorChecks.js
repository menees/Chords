(async () => {
	const check = (condition, message) => { if (!condition) throw new Error(message); };
	const settle = () => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
	try {
		const editor = window.chordBookEditor;
		const fixtures = ['', '[C]Hello', '[C]Hello\r\n[F]World\r\n', 'C   G\r\nWords\nMixed\rFinal',
			'{title: 日本語 🎸}\r\n[C] café\r\n', '<script>alert("not executable")</script>\n[C]Words'];
		for (const original of fixtures) {
			editor.load(original);
			check(editor.getText() === original, 'Loading rewrote source bytes: ' + JSON.stringify(original));
			editor.replaceText('Edited');
			check(editor.getText() === 'Edited', 'Replacement failed');
			check(editor.undo(), 'Replacement was not undoable');
			check(editor.getText() === original, 'Undo did not restore exact original line endings');
			check(editor.redo() && editor.getText() === 'Edited', 'Redo failed');
		}
		editor.load('A\r\nB\r\n');
		editor.replaceText('Changed\nB\n');
		check(editor.getText() === 'Changed\r\nB\r\n', 'Edits lost the dominant newline style');
		editor.setReadOnly(true);
		check(document.querySelector('.cm-content').contentEditable === 'false', 'Read-only editor remained editable');
		editor.setReadOnly(false);
		check(document.querySelector('.cm-content').contentEditable === 'true', 'Editor did not become editable again');
		editor.load('{title: Test}\n{start_of_verse: Verse}\n[C]Words [G7]here\nC   F   G\nSome words\n# comment');
		await settle();
		check(document.querySelectorAll('.cm-chord').length >= 5, 'Inline/above-lyric chords are not highlighted');
		check(document.querySelector('.cm-directive') && document.querySelector('.cm-section') && document.querySelector('.cm-comment'),
			'Directive, section or comment highlighting is missing');
		editor.search();
		check(document.querySelector('.cm-search input'), 'Search/replace panel did not open');
		for (const element of document.querySelectorAll('.cm-search button:not([name=close]), .cm-search label')) {
			const text = element.textContent.trim();
			check(!/^[a-z]/.test(text), 'Search UI caption is not capitalized: ' + text);
		}
		editor.load('Before redo');
		editor.replaceText('After redo');
		editor.undo();
		editor.focus();
		document.activeElement.dispatchEvent(new KeyboardEvent('keydown', {key:'y', code:'KeyY', ctrlKey:true, bubbles:true, cancelable:true}));
		check(editor.getText() === 'After redo', 'Ctrl+Y did not redo');
		editor.undo();
		document.activeElement.dispatchEvent(new KeyboardEvent('keydown', {key:'Z', code:'KeyZ', keyCode:90, ctrlKey:true, shiftKey:true, bubbles:true, cancelable:true}));
		check(editor.getText() === 'After redo', 'Ctrl+Shift+Z did not redo');
		editor.search();
		editor.setUiFont('Segoe UI', 14);
		for (const element of document.querySelectorAll('.cm-search, .cm-search input, .cm-search button, .cm-search label')) {
			const font = getComputedStyle(element);
			check(font.fontFamily.includes('Segoe UI') && font.fontSize === '14px', 'Search widget ignored the window font: ' + element.outerHTML);
		}
		editor.search(true);
		check(document.activeElement.name === 'replace', 'Replace did not focus its input');
		editor.search(false);
		check(document.activeElement.name === 'search', 'Find did not focus its input');
		const { EditorView } = await import('@codemirror/view');
		const { EditorState } = await import('@codemirror/state');
		const { indexSections, sectionAt, sectionText } = await import('./preview-section.mjs');
		const source = '{title: Sample}\n{key: D}\n{capo: 2}\n{define: D base-fret 1 frets x x 0 2 3 2}\n\n{start_of_verse: One}\n[D]First\n\n[D]Second paragraph\n{end_of_verse}\n\n{soc: Chorus}\n[G]Chorus\n{eoc}\n\nPlain final paragraph';
		const doc = EditorState.create({ doc: source }).doc;
		const index = indexSections(doc);
		const verse = sectionAt(index, source.indexOf('First'));
		check(sectionAt(index, source.indexOf('Second paragraph')) === verse, 'Blank lines split an explicit section');
		const preview = sectionText(doc, index, verse);
		check(preview.includes('{key: D}') && preview.includes('{capo: 2}') && preview.includes('{define: D'), 'Preview lost musical context');
		check(preview.includes('Second paragraph') && !preview.includes('Chorus') && !preview.includes('{title:'), 'Preview included another section');
		const chorus = sectionText(doc, index, sectionAt(index, source.indexOf('[G]')));
		check(chorus.includes('{soc: Chorus}') && !chorus.includes('First') && !chorus.includes('Plain final'), 'Abbreviated section selection failed');
		const nestedSource = '{sov: Outer}\n{soc}\n[C]Nested\n{eoc}\nTail\n{eov}\n\nAfter';
		const nestedDoc = EditorState.create({ doc: nestedSource }).doc;
		const nestedIndex = indexSections(nestedDoc);
		check(sectionText(nestedDoc, nestedIndex, sectionAt(nestedIndex, nestedSource.indexOf('Nested'))).includes('Tail'), 'Nested environments split the outer section');
		editor.load(source);
		editor.setPreviewEnabled(true);
		const view = EditorView.findFromDOM(document.querySelector('.cm-editor'));
		view.dispatch({ selection: { anchor: source.indexOf('[G]') } });
		check(editor.getPreviewText() === chorus, 'Caret did not select the current section');
		editor.replaceText('[C]New paragraph\n\n[F]Next paragraph');
		view.dispatch({ selection: { anchor: 0 } });
		check(editor.getPreviewText().includes('New paragraph') && !editor.getPreviewText().includes('Next paragraph'), 'Edit left stale section ranges');
		check(editor.undo(), 'Section preview broke undo');
		check(editor.getText() === source, 'Preview rewrote source');
		editor.setPreviewEnabled(false);
		const huge = Array.from({ length: 10000 }, (_, i) => '[C]Song line ' + i).join('\n');
		const start = performance.now();
		editor.load(huge);
		await settle();
		check(editor.getText() === huge, 'Large document was truncated');
		check(document.querySelectorAll('.cm-line').length < 200, 'Large editor document was not virtualized');
		window.editorLoadMilliseconds = performance.now() - start;
		editor.load('{title: Offline editor}\n{start_of_verse: Verse}\n[C]Sample words with [F]chords above\nG7       C\nChords over text also work\n# Ctrl+F: search; Ctrl+H: replace; Ctrl+Z: undo');
		await settle();
		editor.search(true);
		await settle();
		window.testResult = 'PASS';
	} catch (error) { window.testResult = error.stack || String(error); }
})();
