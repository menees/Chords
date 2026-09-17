// Uses the production formatter stylesheet in WebView2.
(() => {
	try {
		const assert = (value, message) => { if (!value) throw new Error(message); };
		const expected = ['D       Dsus2   Dsus4   D', 'e|-2-------0-------3-------2-------|',
			'B|---3-------3-------3-------3-----|', 'G|-----2-------2-------2-------2---|',
			'D|-------0-------0-------0-------0-|'];
		document.body.innerHTML = '<article class="chord-sheet" style="--line-spacing:0.9">' +
			'<div class="song-column oversize-column" style="min-inline-size:0;inline-size:120px">' +
			expected.map(text => '<pre class="tablature-line">' + text + '</pre>').join('') + '</div></article>';
		const check = () => {
			const column = document.querySelector('.song-column');
			column.scrollLeft = 30;
			return {
				horizontalScroll: column.scrollLeft,
				lines: [...document.querySelectorAll('.tablature-line')].map(line => {
					line.scrollTop = 5;
					line.scrollLeft = 5;
					return { top: line.scrollTop, scroll: line.scrollLeft, whiteSpace: getComputedStyle(line).whiteSpace,
						left: line.getBoundingClientRect().left, text: line.textContent };
				})
			};
		};
		const result = check();
		assert(result.horizontalScroll > 0, 'Containing column scrolls');
		assert(JSON.stringify(result.lines.map(line => line.text)) === JSON.stringify(expected), 'Preserve text');
		for (const line of result.lines) {
			assert(line.top === 0 && line.scroll === 0, 'Individual lines never scroll');
			assert(line.whiteSpace === 'pre', 'Preserve spacing');
			assert(line.left === result.lines[0].left, 'String starts align');
		}
		const style = document.createElement('style');
		style.textContent = '.tablature-line { overflow-x: auto; }';
		document.head.append(style);
		assert(check().lines.some(line => line.top > 0 || line.scroll > 0), 'Fixture detects original regression');
		window.testResult = 'PASS';
	} catch (error) { window.testResult = error.stack || String(error); }
})();