// https://www.meziantou.net/generating-and-downloading-a-file-in-a-blazor-webassembly-application.htm
function BlazorDownloadFile(filename, contentType, content) {
	// Create the URL
	const file = new File([content], filename, { type: contentType });
	const exportUrl = URL.createObjectURL(file);

	// Create the <a> element and click on it
	const a = document.createElement("a");
	document.body.appendChild(a);
	a.href = exportUrl;
	a.download = filename;
	a.target = "_self";
	a.click();

	// We don't need to keep the object URL, let's release the memory
	// On older versions of Safari, it seems you need to comment this line...
	URL.revokeObjectURL(exportUrl);
}

function CopyToClipboard(text, elementId) {
	// The modern navigator.clipboard API is only available in secure contexts (e.g., localhost or HTTPS).
	// If it's not available, we'll try to fallback to the old way to do it.
	if (navigator.clipboard) {
		navigator.clipboard.writeText(text);
	}
	else if (elementId) {
		var element = document.getElementById(elementId);
		if (element) {
			element.select();
			document.execCommand('copy');
		}
	}
}