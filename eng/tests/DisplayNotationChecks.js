// Checks actual production preview DOM, computed visibility and Nashville extension geometry in WebView2.
(() => {
	try {
		const assert = (value, message) => { if (!value) throw new Error(message); };
		const symbols = [...document.querySelectorAll('span.chord')].filter(chord => chord.getClientRects().length > 0);
		assert(JSON.stringify(symbols.map(chord => chord.textContent)) === JSON.stringify(['1', '6-7', '4', '57', '1△']),
			'Preview keeps major, minor and seventh chords visible');
		assert(document.querySelectorAll('.chord-diagram').length === (window.expectedDiagrams ? 1 : 0),
			'Checkbox controls diagrams only');
		assert(symbols.filter(chord => chord.querySelector('sup')).length === 1, 'Only the dominant seventh uses superscript');
		for (const chord of symbols) {
			const extension = chord.querySelector('sup');
			if (extension) {
				assert(chord.textContent === '57' && extension.textContent === '7', 'Only a plain dominant 7 is raised');
				const range = document.createRange();
				range.selectNodeContents(chord.firstChild);
				const baseline = range.getBoundingClientRect();
				assert(extension.getBoundingClientRect().top < baseline.top, 'Extension is visibly raised');
				assert(parseFloat(getComputedStyle(extension).fontSize) < parseFloat(getComputedStyle(chord).fontSize),
					'Extension is smaller than the scale degree');
				assert(extension.getBoundingClientRect().bottom < chord.getBoundingClientRect().bottom + 1,
					'Extension stays within its chord line');
			} else if (chord.textContent === '6-7' || chord.textContent === '1△') {
				assert(chord.childNodes.length === 1 && chord.firstChild.nodeType === Node.TEXT_NODE,
					'Minor seventh and major-seventh triangle remain baseline text');
			}
		}
		window.testResult = 'PASS';
	} catch (error) { window.testResult = error.stack || String(error); }
})();
