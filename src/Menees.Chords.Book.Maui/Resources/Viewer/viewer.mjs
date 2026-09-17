import { getDocument, GlobalWorkerOptions } from './pdfjs/build/pdf.mjs';

const assetUrl = path => new URL('./pdfjs/' + path, import.meta.url).href;
GlobalWorkerOptions.workerSrc = assetUrl('build/pdf.worker.mjs');
const viewport = document.querySelector('#viewport');
const canvas = document.querySelector('#canvas');
const message = document.querySelector('#message');
const pageInput = document.querySelector('#page');
const zoomInput = document.querySelector('#zoom');
let pdf;
let page = 0;
let zoom = 1;
let renderTask;
let renderVersion = 0;
let renderPending = false;
let ready = false;
let pendingPosition;

function getState() {
	const verticalEdge = Math.max(0, viewport.scrollHeight - viewport.clientHeight);
	const horizontalEdge = Math.max(0, viewport.scrollWidth - viewport.clientWidth);
	return {
		page, pageCount: pdf?.numPages || 0, atStart: page === 0 && viewport.scrollTop < 2,
		atEnd: page >= (pdf?.numPages || 1) - 1 && viewport.scrollTop >= verticalEdge - 2, zoom,
		horizontalProgress: horizontalEdge ? viewport.scrollLeft / horizontalEdge : 0,
		verticalProgress: verticalEdge ? viewport.scrollTop / verticalEdge : 0
	};
}

async function render() {
	++renderVersion;
	renderTask?.cancel();
	if (renderPending || !pdf) return;
	renderPending = true;
	try {
		while (pdf) {
			const currentVersion = renderVersion;
			const sheet = await pdf.getPage(page + 1);
			if (currentVersion !== renderVersion) continue;
			const natural = sheet.getViewport({ scale: 1 });
			const fit = Math.min(Math.max(1, viewport.clientWidth - 16) / natural.width, Math.max(1, viewport.clientHeight - 16) / natural.height);
			const display = sheet.getViewport({ scale: fit * zoom });
			// Bound raster memory to 16 MiB even for high-DPI tablets or large pages.
			const ratio = Math.min(window.devicePixelRatio || 1, 2, Math.sqrt(4_194_304 / (display.width * display.height)));
			canvas.width = Math.max(1, Math.floor(display.width * ratio));
			canvas.height = Math.max(1, Math.floor(display.height * ratio));
			canvas.style.width = display.width + 'px';
			canvas.style.height = display.height + 'px';
			renderTask = sheet.render({ canvasContext: canvas.getContext('2d'), viewport: display, transform: [ratio, 0, 0, ratio, 0, 0] });
			try { await renderTask.promise; }
			catch (error) { if (error.name !== 'RenderingCancelledException') throw error; }
			finally { renderTask = null; sheet.cleanup(); }
			if (currentVersion !== renderVersion) continue;
			canvas.hidden = false;
			message.hidden = true;
			if (pendingPosition) {
				viewport.scrollLeft = pendingPosition.horizontalProgress * Math.max(0, viewport.scrollWidth - viewport.clientWidth);
				viewport.scrollTop = pendingPosition.verticalProgress * Math.max(0, viewport.scrollHeight - viewport.clientHeight);
				pendingPosition = null;
			}
			pageInput.value = String(page + 1);
			pageInput.max = String(pdf.numPages);
			document.querySelector('#count').textContent = '/ ' + pdf.numPages;
			ready = true;
			window.dispatchEvent(new Event('menees-chords-layout'));
			if (currentVersion !== renderVersion) continue;
			break;
		}
	} catch (error) {
		message.hidden = false;
		message.textContent = 'Could not display this PDF: ' + error.message;
	} finally { renderPending = false; }
}

function goToPage(index) {
	if (!pdf || !Number.isFinite(index)) return getState();
	const next = Math.max(0, Math.min(pdf.numPages - 1, Math.trunc(index)));
	if (next !== page) {
		page = next;
		viewport.scrollTo(0, 0);
		void render();
	}
	return getState();
}

function moveViewport(direction) {
	if (!ready || renderPending || (direction !== -1 && direction !== 1)) return;
	const edge = Math.max(0, viewport.scrollHeight - viewport.clientHeight);
	if (direction > 0 && viewport.scrollTop < edge - 2) {
		viewport.scrollTop = Math.min(edge, viewport.scrollTop + viewport.clientHeight);
	} else if (direction < 0 && viewport.scrollTop > 2) {
		viewport.scrollTop = Math.max(0, viewport.scrollTop - viewport.clientHeight);
	} else if ((direction < 0 && page === 0) || (direction > 0 && page === pdf.numPages - 1)) {
		window.dispatchEvent(new CustomEvent('menees-chords-boundary', { detail: { direction } }));
	} else {
		pendingPosition = { horizontalProgress: 0, verticalProgress: direction < 0 ? 1 : 0 };
		goToPage(page + direction);
	}
}

function restorePosition(position) {
	zoom = [1, 1.25, 1.5, 2].includes(position.zoom) ? position.zoom : 1;
	pendingPosition = {
		horizontalProgress: Math.max(0, Math.min(1, position.horizontalProgress || 0)),
		verticalProgress: Math.max(0, Math.min(1, position.verticalProgress || 0))
	};
	zoomInput.value = String(zoom);
	goToPage(position.page);
	void render();
}

window.meneesChordsViewer = { getState, goToPage, moveViewport, restorePosition };
document.querySelector('#previous').onclick = () => moveViewport(-1);
document.querySelector('#next').onclick = () => moveViewport(1);
pageInput.onchange = () => goToPage(Number(pageInput.value) - 1);
zoomInput.onchange = () => restorePosition({ page, zoom: Number(zoomInput.value) });
let resizeTimer;
window.addEventListener('resize', () => {
	pendingPosition = getState();
	clearTimeout(resizeTimer);
	resizeTimer = setTimeout(() => void render(), 100);
});

try {
	const generation = new URLSearchParams(location.search).get('generation');
	if (!/^\d+$/.test(generation || '')) throw new Error('Invalid document request.');
	pdf = await getDocument({
		url: '/document/' + generation + '.pdf',
		cMapUrl: assetUrl('cmaps/'), cMapPacked: true,
		standardFontDataUrl: assetUrl('standard_fonts/'), wasmUrl: assetUrl('wasm/'), iccUrl: assetUrl('iccs/'),
		isEvalSupported: false, enableXfa: false, disableRange: true
	}).promise;
	await render();
} catch (error) {
	message.textContent = 'Could not open this PDF: ' + error.message;
}
