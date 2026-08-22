(() => {
	"use strict";

	// Keep iframe-only behavior separate from Script.js, which runs in the host Web app.
	document.addEventListener("keydown", event => {
		if (event.key === "Escape" && window.parent !== window) {
			window.parent.postMessage({ type: "menees-chords-close-html-view" }, "*");
		}
	});
})();
