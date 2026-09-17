// Runs inside the installed WebView2 runtime, launched by Test-NativeViewer.ps1.
(async () => {
	const assert = (value, message) => { if (!value) throw new Error(message); };
	const wait = async predicate => {
		const until = performance.now() + 15000;
		while (!predicate()) {
			if (performance.now() > until) throw new Error('Timed out: ' + predicate);
			await new Promise(resolve => setTimeout(resolve, 25));
		}
	};
	const viewer = window.meneesChordsViewer;
	const state = () => viewer.getState();
	const page = () => document.querySelector('#page').value;
	const move = key => viewer.moveViewport(key === 'PageDown' ? 1 : -1);
	await wait(() => document.querySelector('#page').max === '100');
	assert(state().pageCount === 100, 'Page count');
	assert(document.querySelectorAll('canvas').length === 1, 'One canvas');
	const canvas = document.querySelector('canvas');
	assert(canvas.width * canvas.height <= 4194304, 'Bounded canvas');
	const pixels = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
	let marks = 0;
	for (let i = 0; i < pixels.length; i += 4) if (pixels[i] < 200 && pixels[i + 3] > 0) marks++;
	assert(marks > 100, 'PDF must actually paint text/vector marks');
	const boundaries = [];
	window.addEventListener('menees-chords-boundary', e => boundaries.push(e.detail.direction));
	move('PageUp');
	assert(JSON.stringify(boundaries) === '[-1]', 'Previous boundary');
	move('PageDown');
	await wait(() => page() === '2');
	assert(boundaries.length === 1, 'No premature boundary');
	for (const index of [40, 1, 98, 4]) viewer.goToPage(index);
	await wait(() => page() === '5');
	const oldWidth = innerWidth;
	chrome.webview.postMessage('resize');
	await wait(() => innerWidth !== oldWidth);
	await new Promise(resolve => setTimeout(resolve, 300));
	assert(state().page === 4, 'Resize preserves page');
	viewer.restorePosition({ page: 7, zoom: 2 });
	await wait(() => page() === '8');
	assert(state().zoom === 2, 'Zoom restoration');
	move('PageDown');
	assert(state().page === 7, 'Zoomed move stays on page');
	assert(document.querySelector('#viewport').scrollTop > 0, 'Zoomed viewport moves');
	viewer.restorePosition({ page: 7, zoom: 2, horizontalProgress: 0.5, verticalProgress: 0.75 });
	await wait(() => Math.abs(state().verticalProgress - 0.75) < 0.01);
	assert(Math.abs(state().horizontalProgress - 0.5) < 0.01, 'Horizontal restoration');
	viewer.restorePosition({ page: 7, zoom: 2 });
	await wait(() => state().verticalProgress === 0);
	move('PageUp');
	await wait(() => page() === '7');
	assert(state().verticalProgress > 0.99, 'Reverse page enters bottom');
	viewer.restorePosition({ page: 99, zoom: 1 });
	await wait(() => page() === '100');
	move('PageDown');
	assert(JSON.stringify(boundaries) === '[-1,1]', 'Next boundary');
	assert(window.viewerErrors.length === 0, 'No unhandled JavaScript errors: ' + window.viewerErrors);
	window.testResult = 'PASS';
})().catch(error => { window.testResult = error.stack || String(error); });